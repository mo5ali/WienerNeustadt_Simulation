using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace WienerNeustadtSimulation.Output
{
    public class DashboardGenerator
    {
        public void GenerateFromLog(string logPath, string outputPath)
        {
            Console.WriteLine($"\n📊 Generating dashboard from log: {logPath}");

            if (!File.Exists(logPath))
            {
                Console.WriteLine($"❌ Log file not found: {logPath}");
                return;
            }

            // Parse log file
            var trainJourneys = ParseTrainJourneys(logPath);

            if (trainJourneys.Count == 0)
            {
                Console.WriteLine("⚠ No train events found in log");
                return;
            }

            // Generate HTML
            var html = BuildHTML(trainJourneys);

            var directory = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(outputPath, html);

            Console.WriteLine($"✓ Dashboard saved to: {outputPath}");
            Console.WriteLine($"✓ Processed {trainJourneys.Count} trains");
        }

        private List<TrainJourney> ParseTrainJourneys(string logPath)
        {
            var journeys = new Dictionary<string, TrainJourney>();

            using (var reader = new StreamReader(logPath))
            {
                // Skip header
                reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(';');

                    if (parts.Length < 4) continue;

                    if (parts[0] == "TrainEvent")
                    {
                        string trainId = parts[1];
                        string eventName = parts[2];
                        DateTime simTime;

                        if (!DateTime.TryParse(parts[3], out simTime))
                            continue;

                        string details = parts.Length > 4 ? parts[4] : "";

                        if (!journeys.ContainsKey(trainId))
                        {
                            journeys[trainId] = new TrainJourney
                            {
                                TrainId = trainId,
                                Events = new List<TrainEvent>()
                            };
                        }

                        journeys[trainId].Events.Add(new TrainEvent
                        {
                            Name = FormatEventName(eventName),
                            Time = simTime,
                            Details = details
                        });
                    }
                }
            }

            // Calculate entry/exit times
            foreach (var journey in journeys.Values)
            {
                if (journey.Events.Count > 0)
                {
                    journey.EntryTime = journey.Events.First().Time;
                    journey.ExitTime = journey.Events.Last().Time;
                }
            }

            return journeys.Values.OrderBy(j => j.EntryTime).ToList();
        }

        private string FormatEventName(string eventName)
        {
            // Convert camelCase to readable format
            return System.Text.RegularExpressions.Regex.Replace(eventName, "([a-z])([A-Z])", "$1 $2");
        }

        private string BuildHTML(List<TrainJourney> trains)
        {
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang='en'>");
            sb.AppendLine("<head>");
            sb.AppendLine("    <meta charset='UTF-8'>");
            sb.AppendLine("    <meta name='viewport' content='width=device-width, initial-scale=1.0'>");
            sb.AppendLine("    <title>Train Timeline - Wiener Neustadt Simulation</title>");
            sb.AppendLine("    <style>");
            sb.AppendLine(GetCSS());
            sb.AppendLine("    </style>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");

            sb.AppendLine("    <div class='container'>");
            sb.AppendLine("        <h1>🚂 Train Journey Timeline</h1>");
            sb.AppendLine($"        <p class='subtitle'>Simulation Results - {DateTime.Now:yyyy-MM-dd HH:mm:ss}</p>");

            // Summary stats
            sb.AppendLine("        <div class='summary'>");
            sb.AppendLine($"            <div class='stat-card'>");
            sb.AppendLine($"                <div class='stat-value'>{trains.Count}</div>");
            sb.AppendLine($"                <div class='stat-label'>Total Trains</div>");
            sb.AppendLine($"            </div>");

            if (trains.Any(t => t.TotalTime.TotalMinutes > 0))
            {
                var avgTime = trains.Where(t => t.TotalTime.TotalMinutes > 0)
                                    .Average(t => t.TotalTime.TotalMinutes);
                sb.AppendLine($"            <div class='stat-card'>");
                sb.AppendLine($"                <div class='stat-value'>{avgTime:F1} min</div>");
                sb.AppendLine($"                <div class='stat-label'>Average Transit Time</div>");
                sb.AppendLine($"            </div>");

                var maxTime = trains.Max(t => t.TotalTime.TotalMinutes);
                sb.AppendLine($"            <div class='stat-card'>");
                sb.AppendLine($"                <div class='stat-value'>{maxTime:F1} min</div>");
                sb.AppendLine($"                <div class='stat-label'>Max Transit Time</div>");
                sb.AppendLine($"            </div>");
            }

            sb.AppendLine("        </div>");

            // Train timelines
            foreach (var train in trains)
            {
                sb.AppendLine(BuildTrainCard(train));
            }

            sb.AppendLine("    </div>");
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");

            return sb.ToString();
        }

        private string BuildTrainCard(TrainJourney train)
        {
            var sb = new StringBuilder();

            sb.AppendLine($"        <div class='train-card'>");
            sb.AppendLine($"            <div class='train-header'>");
            sb.AppendLine($"                <h2>🚂 Train {train.TrainId}</h2>");
            if (train.TotalTime.TotalMinutes > 0)
            {
                sb.AppendLine($"                <span class='badge'>Total: {train.TotalTime.TotalMinutes:F1} min</span>");
            }
            sb.AppendLine($"            </div>");

            sb.AppendLine($"            <div class='timeline'>");

            foreach (var evt in train.Events)
            {
                var elapsed = (evt.Time - train.EntryTime).TotalMinutes;
                sb.AppendLine($"                <div class='timeline-item'>");
                sb.AppendLine($"                    <div class='timeline-time'>{evt.Time:HH:mm:ss}</div>");
                sb.AppendLine($"                    <div class='timeline-dot'></div>");
                sb.AppendLine($"                    <div class='timeline-content'>");
                sb.AppendLine($"                        <div class='event-name'>{evt.Name}</div>");
                sb.AppendLine($"                        <div class='event-elapsed'>+{elapsed:F1} min from entry</div>");
                if (!string.IsNullOrEmpty(evt.Details))
                {
                    sb.AppendLine($"                        <div class='event-details'>{evt.Details}</div>");
                }
                sb.AppendLine($"                    </div>");
                sb.AppendLine($"                </div>");
            }

            sb.AppendLine($"            </div>");
            sb.AppendLine($"        </div>");

            return sb.ToString();
        }

        private string GetCSS()
        {
            return @"
        * { margin: 0; padding: 0; box-sizing: border-box; }
        
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            min-height: 100vh;
            padding: 20px;
        }
        
        .container {
            max-width: 1200px;
            margin: 0 auto;
        }
        
        h1 {
            color: white;
            font-size: 2.5em;
            margin-bottom: 10px;
            text-align: center;
            text-shadow: 2px 2px 4px rgba(0,0,0,0.2);
        }
        
        .subtitle {
            color: rgba(255,255,255,0.9);
            text-align: center;
            margin-bottom: 30px;
            font-size: 1.1em;
        }
        
        .summary {
            display: flex;
            gap: 20px;
            margin-bottom: 30px;
            justify-content: center;
            flex-wrap: wrap;
        }
        
        .stat-card {
            background: white;
            padding: 25px 45px;
            border-radius: 12px;
            text-align: center;
            box-shadow: 0 8px 16px rgba(0,0,0,0.15);
            transition: transform 0.2s;
        }
        
        .stat-card:hover {
            transform: translateY(-5px);
        }
        
        .stat-value {
            font-size: 2.5em;
            font-weight: bold;
            color: #667eea;
            line-height: 1.2;
        }
        
        .stat-label {
            color: #666;
            margin-top: 8px;
            font-size: 0.95em;
            font-weight: 500;
        }
        
        .train-card {
            background: white;
            border-radius: 12px;
            padding: 30px;
            margin-bottom: 25px;
            box-shadow: 0 8px 16px rgba(0,0,0,0.15);
            transition: transform 0.2s;
        }
        
        .train-card:hover {
            transform: translateX(5px);
        }
        
        .train-header {
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 25px;
            padding-bottom: 20px;
            border-bottom: 3px solid #f0f0f0;
        }
        
        .train-header h2 {
            color: #333;
            font-size: 1.6em;
        }
        
        .badge {
            background: linear-gradient(135deg, #667eea 0%, #764ba2 100%);
            color: white;
            padding: 8px 20px;
            border-radius: 25px;
            font-size: 0.95em;
            font-weight: 600;
            box-shadow: 0 4px 8px rgba(102, 126, 234, 0.3);
        }
        
        .timeline {
            position: relative;
            padding-left: 140px;
        }
        
        .timeline::before {
            content: '';
            position: absolute;
            left: 15px;
            top: 10px;
            bottom: 10px;
            width: 3px;
            background: linear-gradient(to bottom, #667eea, #764ba2);
        }
        
        .timeline-item {
            position: relative;
            display: flex;
            margin-bottom: 25px;
            align-items: flex-start;
        }
        
        .timeline-item:last-child {
            margin-bottom: 0;
        }
        
        .timeline-time {
            position: absolute;
            left: -125px;
            top: 2px;
            font-weight: 700;
            color: #667eea;
            font-family: 'Courier New', monospace;
            font-size: 0.95em;
        }
        
        .timeline-dot {
            position: absolute;
            left: -132px;
            top: 7px;
            width: 14px;
            height: 14px;
            border-radius: 50%;
            background: white;
            border: 4px solid #667eea;
            box-shadow: 0 0 0 3px rgba(102, 126, 234, 0.2);
            z-index: 1;
        }
        
        .timeline-content {
            flex: 1;
            background: #f8f9fa;
            padding: 12px 16px;
            border-radius: 8px;
            border-left: 4px solid #667eea;
        }
        
        .event-name {
            font-weight: 600;
            color: #333;
            margin-bottom: 5px;
            font-size: 1.05em;
        }
        
        .event-elapsed {
            font-size: 0.85em;
            color: #888;
            font-style: italic;
        }
        
        .event-details {
            font-size: 0.9em;
            color: #666;
            margin-top: 5px;
            padding-top: 5px;
            border-top: 1px solid #e0e0e0;
        }
            ";
        }
    }

    public class TrainJourney
    {
        public string TrainId { get; set; }
        public DateTime EntryTime { get; set; }
        public DateTime ExitTime { get; set; }
        public List<TrainEvent> Events { get; set; }

        public TimeSpan TotalTime => ExitTime - EntryTime;
    }

    public class TrainEvent
    {
        public string Name { get; set; }
        public DateTime Time { get; set; }
        public string Details { get; set; }
    }
}