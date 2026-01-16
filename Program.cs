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
                Console.WriteLine($"✓ Loaded {root.WagonGroups?.Count ?? 0} wagon groups\n");

                // Create simulation engine
                var engine = new SimulationEngine();

                // Initialize infrastructure
                Console.WriteLine("[Initializing infrastructure...]");
                var arrivalTracks = CreateArrivalTracks();
                var classificationTracks = CreateClassificationTracks();
                Console.WriteLine($"✓ Created {arrivalTracks.Count} arrival tracks");
                Console.WriteLine($"✓ Created {classificationTracks.Count} classification tracks\n");

                // Parse wagon group data from input
                var wagonGroupData = ParseWagonGroupData(root);

                // Create control units (in reverse dependency order)
                Console.WriteLine("[Initializing control units...]");
                var classificationControl = new ClassificationControlUnit(engine);

                var arrivalControl = new ArrivalControlUnit(
                    engine,
                    classificationControl,
                    classificationTracks,
                    wagonGroupData
                );

                var entryControl = new EntryControlUnit(
                    engine,
                    arrivalControl,
                    arrivalTracks
                );
                Console.WriteLine("✓ EntryControlUnit initialized");
                Console.WriteLine("✓ ArrivalControlUnit initialized");
                Console.WriteLine("✓ ClassificationControlUnit initialized\n");

                // Schedule inbound train arrivals
                Console.WriteLine("📅 Scheduling train arrivals...");
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
                        () => entryControl.HandleTrainArrival(t, simTime.ToUniversalTime()),
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

                Console.WriteLine("\n═══════════════════════════════════════════════════════════");
                Console.WriteLine("                   SIMULATION COMPLETE                     ");
                Console.WriteLine("═══════════════════════════════════════════════════════════\n");

                Console.WriteLine("✓ Simulation finished successfully");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"\n❌ Fatal Error: {ex.Message}");
                Console.Error.WriteLine(ex.StackTrace);
                return 99;
            }
        }

        static List<Track> CreateArrivalTracks()
        {
            return new List<Track>
            {
                new Track("2001", 400) { Designation = "Arrival", RealLifeID = "A1", Area = "Arrival" },
                new Track("2002", 400) { Designation = "Arrival", RealLifeID = "A2", Area = "Arrival" },
                new Track("2003", 450) { Designation = "Arrival", RealLifeID = "A3", Area = "Arrival" }
            };
        }

        static List<Track> CreateClassificationTracks()
        {
            return new List<Track>
            {
                new Track("3001", 500) { Designation = "Classification", RealLifeID = "C1", Area = "Classification" },
                new Track("3002", 500) { Designation = "Classification", RealLifeID = "C2", Area = "Classification" },
                new Track("3003", 500) { Designation = "Classification", RealLifeID = "C3", Area = "Classification" },
                new Track("3004", 500) { Designation = "Classification", RealLifeID = "C4", Area = "Classification" },
                new Track("3005", 500) { Designation = "Classification", RealLifeID = "C5", Area = "Classification" }
            };
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
    }
}