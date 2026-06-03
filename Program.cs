using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Control;
using WienerNeustadtSimulation.Models;
using WienerNeustadtSimulation.Infrastructure;
using System.Runtime.InteropServices;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Output;

namespace WienerNeustadtSimulation
{
    class Program
    {
        static int Main(string[] args)
        {
            try
            {
                Console.WriteLine("╔════════════════════════════════════════════════════════════╗");
                Console.WriteLine("║   Wiener Neustadt Train Shunting Yard Simulation           ║");
                Console.WriteLine("╚════════════════════════════════════════════════════════════╝\n");

                // Initialize logger
                var outputFolder = Path.Combine(AppContext.BaseDirectory, "OutputFiles");
                var logPath = Path.Combine(outputFolder, "SimulationLog.csv");
                SimulationLogger.Instance.Initialize(logPath);
                Console.WriteLine($"📝 Logging to: {logPath}\n");

                // Load input data
                var inboundPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "InputFiles", "InboundTrains_test2.json");
                if (!File.Exists(inboundPath))
                {
                    Console.Error.WriteLine($"   Inbound file not found: {inboundPath}");
                    Console.Error.WriteLine($"   Current working directory: {Directory.GetCurrentDirectory()}");
                    Console.Error.WriteLine($"   Application directory: {AppContext.BaseDirectory}");
                    return 2;
                }

                Console.WriteLine($"[Load incoming trains file: {inboundPath}]");
                var json = File.ReadAllText(inboundPath);
                var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonSerializer.Deserialize<InboundRoot>(json, opts)
                           ?? throw new Exception("Failed to parse inbound JSON");

                Console.WriteLine($"✓ Loaded {root.InboundTrains?.Count ?? 0} inbound trains");
                Console.WriteLine($"✓ Loaded {root.WagonGroups?.Count ?? 0} wagon groups");

                // Load infrastructure data
                var infrastructurePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputFiles", "Infrastructure_WienerNeustadt_V20.json");
                Console.WriteLine($"📂 Loading infrastructure file: {infrastructurePath}");
                var infrastructureJson = File.ReadAllText(infrastructurePath);
                var infrastructureRoot = JsonSerializer.Deserialize<InfrastructureRoot>(infrastructureJson, opts)
                           ?? throw new Exception("Failed to parse infrastructure JSON");

                Console.WriteLine($"✓ Loaded {infrastructureRoot.TrackSegments?.Count ?? 0} track segments\n");


                // Load resource pool
                var resourcePoolPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InputFiles", "ResourcePool.json");
                Console.WriteLine($"📂 Loading resource pool file: {resourcePoolPath}");
                var resourcePoolJson = File.ReadAllText(resourcePoolPath);
                var resourcePool = JsonSerializer.Deserialize<ResourcePoolRoot>(resourcePoolJson, opts)
                           ?? throw new Exception("Failed to parse resource pool JSON");

                Console.WriteLine($"✓ Loaded {resourcePool.Workers?.Count ?? 0} workers");
                Console.WriteLine($"✓ Loaded {resourcePool.ShuntingLocomotives?.Count ?? 0} shunting locomotives\n");
                // NEW: write static resource metadata into the CSV (for the visualizer)
                SimulationLogger.Instance.WriteResourceMetadata(
                    resourcePool,
                    trainLocoCount: 100,
                    exitGateCount: 1,
                    shuntingLocoSpeedMetersPerMinute: 25.0
                );

                // Create simulation engine
                var engine = new SimulationEngine();

                // Initialize infrastructure
                Console.WriteLine("[Initializing infrastructure...]");
                var arrivalTracks = CreateArrivalTracks(infrastructureRoot);
                var classificationTracks = CreateClassificationTracks(infrastructureRoot);
                Console.WriteLine($"✓ Created {arrivalTracks.Count} arrival tracks");
                Console.WriteLine($"✓ Created {classificationTracks.Count} classification tracks\n");

                // Parse wagon group data from input
                var wagonGroupData = ParseWagonGroupData(root);

                // Parse wagon data
                var wagonData = ParseWagonData(root);

                // Calculate wagon group lengths from their wagons
                foreach (var wg in wagonGroupData.Values)
                {
                    if (wg.WagonIds != null && wg.WagonIds.Count > 0)
                    {
                        double calculatedLength = 0;
                        foreach (var wagonId in wg.WagonIds)
                        {
                            if (wagonData.ContainsKey(wagonId))
                            {
                                calculatedLength += wagonData[wagonId].Length ?? 0;
                            }
                        }
                        wg.Length = calculatedLength;
                        Console.WriteLine($"  → WagonGroup {wg.ID}: {wg.WagonIds.Count} wagons = {calculatedLength}m");
                    }
                }

                SimulationLogger.Instance.WriteInboundMetadata(root);

                // Create control units (in correct dependency order!)
                Console.WriteLine("\n[Initializing control units...]");

                // 1. ResourceControlUnit (no dependencies)
                var resourceControl = new ResourceControlUnit(engine, resourcePool);
                Console.WriteLine("✓ ResourceControlUnit initialized");

                // 2. ClassificationControlUnit (depends on ResourceCU)
                var classificationControl = new ClassificationControlUnit(
                    engine,
                    resourceControl,
                    classificationTracks,
                    wagonGroupData
                );
                Console.WriteLine("✓ ClassificationControlUnit initialized");

                // 3. ArrivalControlUnit (depends on both ResourceCU and ClassificationCU)
                var arrivalControl = new ArrivalControlUnit(
                    engine,
                    classificationControl,
                    resourceControl,
                    arrivalTracks,
                    classificationTracks,
                    wagonGroupData
                );
                Console.WriteLine("✓ ArrivalControlUnit initialized");

                // Wire the back-edge: when an OBT is created on a classification
                // track, the destination is no longer eligible to be sorted to
                // that same track, so ArrivalCU drops the mapping. New WGs of
                // that destination get a fresh track assignment on the next
                // RunSortingMethod call.
                classificationControl.DestinationCommittedToOutbound += (destination, _) =>
                    arrivalControl.ReleaseDestinationMapping(destination);
                Console.WriteLine("✓ Wired DestinationCommittedToOutbound → ReleaseDestinationMapping\n");



                // Schedule inbound train arrivals
                Console.WriteLine("[Scheduling train arrivals...]");
                int scheduledCount = 0;
                foreach (var t in root.InboundTrains ?? new List<TrainDto>())
                {
                    if (string.IsNullOrWhiteSpace(t.Time))
                    {
                        Console.WriteLine($"[Skipping train {t.ID} - no Time provided]");
                        continue;
                    }

                    if (!DateTime.TryParse(t.Time, CultureInfo.InvariantCulture,
                        DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var simTime))
                    {
                        Console.WriteLine($"[Skipping train {t.ID} - invalid Time format: {t.Time}]");
                        continue;
                    }

                    engine.Schedule(
                        simTime.ToUniversalTime(),
                        () => arrivalControl.HandleTrainArrival(t, simTime.ToUniversalTime()),
                        $"TrainArrives-{t.ID}"
                    );
                    scheduledCount++;
                }
                Console.WriteLine($"✓ Scheduled {scheduledCount} train arrival events\n");
                // we will not consider wagons for now, so dont even mention them, so no "0 wagons"
                //instead of this 
                //01.01.2025 - 09:26:20 | ResourceCU: Charles traveling to track 703(100m 75s) for 'Act_PO_250101092620_ArrivalCU_12341'
                //01.01.2025 - 09:26:20 | ResourceCU: Lewis traveling to track 703(100m 75s) for 'Act_PO_250101092620_ArrivalCU_12341'
                //01.01.2025 - 09:26:20 | ResourceCU: Max traveling to track 703(100m 75s) for 'Act_PO_250101092620_ArrivalCU_12341'
                //make this
                //01.01.2025 - 09:26:20 | ResourceCU: Charles(75s100m) Lewis(75s100m) Max(75s100m) traveling to track 703 for 'Act_PO_250101092620_ArrivalCU_12341'
                // remove these (dont mention the check at all unless it is positive (train completed))
                //01.01.2025 - 09:37:06 | ClassifCU: checking completion for track 615
                //01.01.2025 - 09:37:06 | ClassifCU: track 615 has 1 WGs, total length = 32, 0m, 0 wagons
                //01.01.2025 - 09:37:06 | ClassifCU: train not yet complete(32, 0m < 100m)
                //make those one line
                //01.01.2025 - 09:43:52 | ClassifCU: DONE 'Act_SEC_250101093535_ClassificationCU_1234102'
                //01.01.2025 - 09:43:52 | ClassifCU: 1234102 is now SECURED
                //so they become
                //01.01.2025 - 09:43:52 | ClassifCU: DONE 'Act_SEC_250101093535_ClassificationCU_1234102' 1234102 is SECURED
                //when an activity in done dont do this
                //01.01.2025-09:38:00 | ArrivalCU: DONE 'Act_ITP_250101090600_ArrivalCU_12342' for train 12342
                //01.01.2025 - 09:38:00 | ResourceCU: Fernando returned to pool
                //01.01.2025 - 09:38:00 | ResourceCU: George returned to pool
                //01.01.2025 - 09:38:00 | ResourceCU: Lando returned to pool
                // if none of the workers are immediatly aclocated to another task say..
                //01.01.2025-09:38:00 | ArrivalCU: DONE 'Act_ITP_250101090600_ArrivalCU_12342' for train 12342
                //01.01.2025 - 09:38:00 | ResourceCU: Fernando, George, Lando become available, returning to waiting area
                // Run simulation
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine("                    SIMULATION START                       ");
                Console.WriteLine("═══════════════════════════════════════════════════════════\n");

                engine.Run();

                // Close logger
                SimulationLogger.Instance.Close();

                // Run the Python analytics script (Step 1 — process durations).
                // Wrapped in try/catch so a missing Python interpreter or a
                // missing openpyxl install just prints a warning instead of
                // failing the whole simulation.
                RunPythonAnalytics();

                Console.WriteLine("\n═══════════════════════════════════════════════════════════");
                Console.WriteLine("                   SIMULATION COMPLETE                     ");
                Console.WriteLine("════════════════════════════════════════════════════════════\n");

                Console.WriteLine("✓ Simulation finished successfully");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n Fatal Error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 99;
            }
        }

        // Spawns Output/analytics.py to refresh SimulationAnalytics.xlsx.
        // analytics.py resolves its own input/output paths off __file__, so we
        // just locate the script and invoke it. We try `python` first then
        // `python3` (Linux/macOS default name) so this works on both platforms
        // without configuration. Errors are printed but do not fail the run —
        // the C# sim itself has already produced SimulationLog.csv at this
        // point, so the user can always re-run analytics manually.
        static void RunPythonAnalytics()
        {
            try
            {
                // bin/Debug/net8.0/  →  ../../../Output/analytics.py
                var scriptPath = Path.GetFullPath(
                    Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Output", "analytics.py"));

                if (!File.Exists(scriptPath))
                {
                    Console.WriteLine($"⚠ analytics.py not found at {scriptPath} — skipping.");
                    return;
                }

                Console.WriteLine($"\n📊 Running analytics: {scriptPath}");

                foreach (var interpreter in new[] { "python", "python3", "py" })
                {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = interpreter,
                            Arguments = $"\"{scriptPath}\"",
                            RedirectStandardOutput = true,
                            RedirectStandardError = true,
                            UseShellExecute = false,
                            CreateNoWindow = true,
                        };
                        using var proc = System.Diagnostics.Process.Start(psi);
                        if (proc == null) continue;

                        proc.WaitForExit(60_000);
                        var stdout = proc.StandardOutput.ReadToEnd();
                        var stderr = proc.StandardError.ReadToEnd();
                        if (!string.IsNullOrWhiteSpace(stdout)) Console.Write(stdout);
                        if (!string.IsNullOrWhiteSpace(stderr)) Console.Error.Write(stderr);

                        if (proc.ExitCode != 0)
                            Console.Error.WriteLine($"⚠ analytics.py exited with code {proc.ExitCode} (interpreter '{interpreter}')");
                        return;  // success or non-zero exit; either way, don't try the next interpreter
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                        // Interpreter not found on PATH; try the next one.
                        continue;
                    }
                }

                Console.Error.WriteLine("⚠ Could not find a Python interpreter on PATH (tried: python, python3, py).");
                Console.Error.WriteLine("   Install Python 3 and `pip install openpyxl`, then re-run, or invoke analytics.py manually.");
            }
            catch (Exception aex)
            {
                Console.Error.WriteLine($"⚠ Could not run analytics.py: {aex.Message}");
            }
        }

        static List<Track> CreateArrivalTracks(InfrastructureRoot infrastructureRoot)
        {
            var tracks = new List<Track>();
            var arrivalSegments = infrastructureRoot.TrackSegments?
                .Where(ts => ts.RailwayStationArea == "ArrivalArea")
                .ToList() ?? new List<TrackSegmentDto>();

            foreach (var segment in arrivalSegments)
            {
                var track = new Track(segment.TrackId.ToString("D4"), segment.Length)
                {
                    RealLifeID = segment.GetMapIdAsString(),
                    // Use the canonical short form ("Arrival") rather than the
                    // RailwayStationArea suffix ("ArrivalArea") from the JSON.
                    // Activities, workers, and the area filter all compare on
                    // this short form, so anything that takes its area from a
                    // Track (ITP, PushOff, ArrivalDrive) lines up correctly.
                    Area = "Arrival",
                    Designation = "Arrival",
                    SegmentIds = new List<string> { segment.Id.ToString() }
                };
                tracks.Add(track);
            }

            return tracks;
        }

        static List<Track> CreateClassificationTracks(InfrastructureRoot infrastructureRoot)
        {
            var tracks = new List<Track>();
            var classificationSegments = infrastructureRoot.TrackSegments?
                .Where(ts => ts.RailwayStationArea == "ClassificationArea")
                .ToList() ?? new List<TrackSegmentDto>();

            foreach (var segment in classificationSegments)
            {
                var track = new Track(segment.TrackId.ToString("D4"), segment.Length)
                {
                    RealLifeID = segment.GetMapIdAsString(),
                    // Canonical short form — see CreateArrivalTracks for the why.
                    Area = "Classification",
                    Designation = "Classification",
                    SegmentIds = new List<string> { segment.Id.ToString() }
                };
                tracks.Add(track);
            }

            return tracks;
        }

        static Dictionary<string, WagonGroupDto> ParseWagonGroupData(InboundRoot root)
        {
            var dict = new Dictionary<string, WagonGroupDto>();

            if (root.WagonGroups != null)
            {
                foreach (var wg in root.WagonGroups)
                {
                    if (wg.ID != null)
                        dict[wg.ID] = wg;
                }
            }

            return dict;
        }

        static Dictionary<string, WagonDto> ParseWagonData(InboundRoot root)
        {
            var dict = new Dictionary<string, WagonDto>();

            if (root.Wagons != null)
            {
                foreach (var wagon in root.Wagons)
                {
                    if (wagon.ID != null)
                        dict[wagon.ID] = wagon;
                }
            }

            return dict;
        }
    }
}