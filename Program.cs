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

                // Load input data
                var inboundPath = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "InputFiles", "InboundTrains.json");
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

                // NEW: Parse wagon data
                var wagonData = ParseWagonData(root);

                // NEW: Calculate wagon group lengths from their wagonsnhzn
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

                // Create control units (in reverse dependency order)
                Console.WriteLine("[Initializing control units...]");
                var classificationControl = new ClassificationControlUnit(engine);

                var resourceControl = new ResourceControlUnit(engine, resourcePool);
                Activity.SetResourceControlUnit(resourceControl);

                // ArrivalControlUnit now handles BOTH entry and arrival phases
                var arrivalControl = new ArrivalControlUnit(
                    engine,
                    classificationControl,
                    resourceControl,
                    arrivalTracks,
                    classificationTracks,
                    wagonGroupData
                );


                Console.WriteLine("✓ ArrivalControlUnit initialized");
                Console.WriteLine("✓ ClassificationControlUnit initialized");
                Console.WriteLine("✓ ResourceControlUnit initialized\n");


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

                // Run simulation
                Console.WriteLine("═══════════════════════════════════════════════════════════");
                Console.WriteLine("                    SIMULATION START                       ");
                Console.WriteLine("═══════════════════════════════════════════════════════════\n");

                engine.Run();

                // Print activity summary
                ActivityRegistry.Instance.PrintSummary();

                // Export activities to JSON
                var outputFolder = Path.Combine(AppContext.BaseDirectory, "OutputFiles");
                Directory.CreateDirectory(outputFolder);
                var activityLogPath = Path.Combine(outputFolder, "ActivityLog.json");
                ActivityRegistry.Instance.ExportToJson(activityLogPath);

                Console.WriteLine("\n═══════════════════════════════════════════════════════════");
                Console.WriteLine("                   SIMULATION COMPLETE                     ");
                Console.WriteLine("═════════════════════════════════════════��═════════════════\n");

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
                    Area = segment.RailwayStationArea ?? "",
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
                    Area = segment.RailwayStationArea ?? "",
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