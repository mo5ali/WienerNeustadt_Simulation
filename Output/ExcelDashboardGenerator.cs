using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;

namespace WienerNeustadtSimulation.Output
{
    public class ExcelDashboardGenerator
    {
        public void GenerateFromLog(string logPath, string outputPath)
        {
            var (incomingTrains, wagonGroups, outboundTrains, workers, locomotives) = ParseLog(logPath);

            using (var package = new ExcelPackage())
            {
                // Sheet 1: Incoming Trains
                CreateIncomingTrainsSheet(package, incomingTrains);

                // Sheet 2: Wagon Groups
                CreateWagonGroupsSheet(package, wagonGroups);

                // Sheet 3: Outbound Trains
                CreateOutboundTrainsSheet(package, outboundTrains);

                // Sheet 4: Workers
                CreateWorkersSheet(package, workers);

                // Sheet 5: Shunting Locomotives
                CreateLocomotivesSheet(package, locomotives);

                // Save file
                var fileInfo = new FileInfo(outputPath);
                package.SaveAs(fileInfo);
            }

            Console.WriteLine($"📊 Excel dashboard generated: {outputPath}");
            Console.WriteLine($"   ✓ {incomingTrains.Count} incoming trains");
            Console.WriteLine($"   ✓ {wagonGroups.Count} wagon groups");
            Console.WriteLine($"   ✓ {outboundTrains.Count} outbound trains");
            Console.WriteLine($"   ✓ {workers.Count} worker activities");
            Console.WriteLine($"   ✓ {locomotives.Count} locomotive activities");
        }

        private void CreateIncomingTrainsSheet(ExcelPackage package, List<IncomingTrainJourney> trains)
        {
            var worksheet = package.Workbook.Worksheets.Add("Incoming Trains");

            var headers = new[]
            {
                "Train ID",
                "Enters system (Entry)",
                "Assigned arrival track",
                "Arrives at arrival track",
                "Preparation requested",
                "Classification determined",
                "Preparation started",
                "Preparation completed",
                "Push-off requested",
                "Push-off started",
                "Push-off completed",
                "Exited system"
            };

            WriteHeaders(worksheet, headers);

            int row = 2;
            foreach (var train in trains.OrderBy(t => t.GetEventTime("Entry")))
            {
                worksheet.Cells[row, 1].Value = train.TrainId;
                worksheet.Cells[row, 2].Value = FormatTimestamp(train.GetEventTime("Entry"));
                worksheet.Cells[row, 3].Value = FormatTimestamp(train.GetEventTime("AssignedArrivalTrack"));
                worksheet.Cells[row, 4].Value = FormatTimestamp(train.GetEventTime("ArrivedArrivalTrack"));
                worksheet.Cells[row, 5].Value = FormatTimestamp(train.GetEventTime("PreparationRequested"));
                worksheet.Cells[row, 6].Value = FormatTimestamp(train.GetEventTime("ClassificationTrackAssigned"));
                worksheet.Cells[row, 7].Value = FormatTimestamp(train.GetEventTime("PreparationStarted"));
                worksheet.Cells[row, 8].Value = FormatTimestamp(train.GetEventTime("PreparationComplete"));
                worksheet.Cells[row, 9].Value = FormatTimestamp(train.GetEventTime("PushOffRequested"));
                worksheet.Cells[row, 10].Value = FormatTimestamp(train.GetEventTime("PushOffStarted"));
                worksheet.Cells[row, 11].Value = FormatTimestamp(train.GetEventTime("PushOffComplete"));
                worksheet.Cells[row, 12].Value = FormatTimestamp(train.GetEventTime("ExitedSystem"));
                row++;
            }

            FormatWorksheet(worksheet, headers.Length);
        }

        private void CreateWagonGroupsSheet(ExcelPackage package, List<WagonGroupJourney> wagonGroups)
        {
            var worksheet = package.Workbook.Worksheets.Add("Wagon Groups");

            var headers = new[]
            {
                "Wagon Group ID",
                "Parent Train ID",
                "Destination",
                "Pushed to classification track",
                "Arrived at classification track",
                "Entity created",
                "Securing/Coupling requested",
                "Securing/Coupling started",
                "Secured/Coupled",
                "Added to outbound train"
            };

            WriteHeaders(worksheet, headers);

            int row = 2;
            foreach (var wg in wagonGroups.OrderBy(w => w.GetEventTime("ArrivedClassificationTrack")))
            {
                worksheet.Cells[row, 1].Value = wg.WagonGroupId;
                worksheet.Cells[row, 2].Value = wg.ParentTrainId;
                worksheet.Cells[row, 3].Value = wg.Destination;
                worksheet.Cells[row, 4].Value = FormatTimestamp(wg.GetEventTime("PushingToTrack"));
                worksheet.Cells[row, 5].Value = FormatTimestamp(wg.GetEventTime("ArrivedClassificationTrack"));
                worksheet.Cells[row, 6].Value = FormatTimestamp(wg.GetEventTime("EntityCreated"));
                worksheet.Cells[row, 7].Value = FormatTimestamp(wg.GetEventTime("SecuringRequested") ?? wg.GetEventTime("CouplingRequested"));
                worksheet.Cells[row, 8].Value = FormatTimestamp(wg.GetEventTime("SecuringActivityStarted") ?? wg.GetEventTime("CouplingActivityStarted"));
                worksheet.Cells[row, 9].Value = FormatTimestamp(wg.GetEventTime("Secured") ?? wg.GetEventTime("Coupled"));
                worksheet.Cells[row, 10].Value = ""; // TODO: Add if we log this
                row++;
            }

            FormatWorksheet(worksheet, headers.Length);
        }

        private void CreateOutboundTrainsSheet(ExcelPackage package, List<OutboundTrainJourney> trains)
        {
            var worksheet = package.Workbook.Worksheets.Add("Outbound Trains");

            var headers = new[]
            {
                "Train ID",
                "Destination",
                "Total Length (m)",
                "Number of Wagon Groups",
                "Train complete (criteria met)",
                "Outbound train created",
                "Locomotive requested",
                "Locomotive coupling started",
                "Locomotive coupled",
                "Leaving prep requested",
                "Leaving prep started",
                "Leaving prep completed",
                "Departure processing",
                "Departure drive started",
                "Departed (exited system)"
            };

            WriteHeaders(worksheet, headers);

            int row = 2;
            foreach (var train in trains.OrderBy(t => t.GetEventTime("OutboundTrainCreated")))
            {
                worksheet.Cells[row, 1].Value = train.TrainId;
                worksheet.Cells[row, 2].Value = train.Destination;
                worksheet.Cells[row, 3].Value = train.TotalLength;
                worksheet.Cells[row, 4].Value = train.WagonGroupCount;
                worksheet.Cells[row, 5].Value = FormatTimestamp(train.GetEventTime("TrainComplete"));
                worksheet.Cells[row, 6].Value = FormatTimestamp(train.GetEventTime("OutboundTrainCreated"));
                worksheet.Cells[row, 7].Value = FormatTimestamp(train.GetEventTime("LocomotiveRequested"));
                worksheet.Cells[row, 8].Value = FormatTimestamp(train.GetEventTime("LocoCouplingStarted"));
                worksheet.Cells[row, 9].Value = FormatTimestamp(train.GetEventTime("LocomotiveCoupled"));
                worksheet.Cells[row, 10].Value = FormatTimestamp(train.GetEventTime("LeavingPrepRequested"));
                worksheet.Cells[row, 11].Value = FormatTimestamp(train.GetEventTime("LeavingPrepStarted"));
                worksheet.Cells[row, 12].Value = FormatTimestamp(train.GetEventTime("LeavingPrepComplete"));
                worksheet.Cells[row, 13].Value = FormatTimestamp(train.GetEventTime("DepartureProcessing"));
                worksheet.Cells[row, 14].Value = FormatTimestamp(train.GetEventTime("DepartureStarted"));
                worksheet.Cells[row, 15].Value = FormatTimestamp(train.GetEventTime("Departed"));
                row++;
            }

            FormatWorksheet(worksheet, headers.Length);
        }

        private void CreateWorkersSheet(ExcelPackage package, List<WorkerActivity> workers)
        {
            var worksheet = package.Workbook.Worksheets.Add("Workers");

            var headers = new[]
            {
                "Worker ID",
                "Activity ID",
                "Activity Location",
                "Allocated to activity",
                "Arrived at location",
                "Task completed",
                "Returned to pool"
            };

            WriteHeaders(worksheet, headers);

            int row = 2;
            foreach (var worker in workers.OrderBy(w => w.AllocatedAt))
            {
                worksheet.Cells[row, 1].Value = worker.WorkerId;
                worksheet.Cells[row, 2].Value = worker.ActivityId;
                worksheet.Cells[row, 3].Value = worker.Location;
                worksheet.Cells[row, 4].Value = FormatTimestamp(worker.AllocatedAt);
                worksheet.Cells[row, 5].Value = FormatTimestamp(worker.ArrivedAt);
                worksheet.Cells[row, 6].Value = FormatTimestamp(worker.CompletedAt);
                worksheet.Cells[row, 7].Value = FormatTimestamp(worker.ReturnedAt);
                row++;
            }

            FormatWorksheet(worksheet, headers.Length);
        }

        private void CreateLocomotivesSheet(ExcelPackage package, List<LocomotiveActivity> locomotives)
        {
            var worksheet = package.Workbook.Worksheets.Add("Shunting Locomotives");

            var headers = new[]
            {
                "Locomotive ID",
                "Activity ID",
                "Activity Location",
                "Allocated to activity",
                "Arrived at location",
                "Task completed",
                "Returned to pool"
            };

            WriteHeaders(worksheet, headers);

            int row = 2;
            foreach (var loco in locomotives.OrderBy(l => l.AllocatedAt))
            {
                worksheet.Cells[row, 1].Value = loco.LocomotiveId;
                worksheet.Cells[row, 2].Value = loco.ActivityId;
                worksheet.Cells[row, 3].Value = loco.Location;
                worksheet.Cells[row, 4].Value = FormatTimestamp(loco.AllocatedAt);
                worksheet.Cells[row, 5].Value = FormatTimestamp(loco.ArrivedAt);
                worksheet.Cells[row, 6].Value = FormatTimestamp(loco.CompletedAt);
                worksheet.Cells[row, 7].Value = FormatTimestamp(loco.ReturnedAt);
                row++;
            }

            FormatWorksheet(worksheet, headers.Length);
        }

        private void WriteHeaders(ExcelWorksheet worksheet, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                worksheet.Cells[1, i + 1].Value = headers[i];
            }
        }

        private void FormatWorksheet(ExcelWorksheet worksheet, int columnCount)
        {
            // Style header
            using (var range = worksheet.Cells[1, 1, 1, columnCount])
            {
                range.Style.Font.Bold = true;
                range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
                range.Style.Font.Color.SetColor(Color.White);
                range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
            }

            // Auto-fit columns
            worksheet.Cells.AutoFitColumns();

            // Set minimum column width
            for (int col = 1; col <= columnCount; col++)
            {
                if (worksheet.Column(col).Width < 15)
                    worksheet.Column(col).Width = 15;
            }

            // Freeze header row
            worksheet.View.FreezePanes(2, 1);
        }

        private string FormatTimestamp(DateTime? timestamp)
        {
            if (!timestamp.HasValue)
                return "";

            return timestamp.Value.ToString("dd.MM.yyyy HH:mm:ss");
        }

        private (List<IncomingTrainJourney>, List<WagonGroupJourney>, List<OutboundTrainJourney>, List<WorkerActivity>, List<LocomotiveActivity>) ParseLog(string logPath)
        {
            var incomingTrains = new Dictionary<string, IncomingTrainJourney>();
            var wagonGroups = new Dictionary<string, WagonGroupJourney>();
            var outboundTrains = new Dictionary<string, OutboundTrainJourney>();
            var workerActivities = new Dictionary<string, WorkerActivity>();
            var locoActivities = new Dictionary<string, LocomotiveActivity>();

            using (var reader = new StreamReader(logPath))
            {
                // Skip header
                reader.ReadLine();

                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split(';');
                    if (parts.Length < 4) continue;

                    string eventType = parts[0];
                    string entityId = parts[1];
                    string eventName = parts[2];
                    DateTime simTime;

                    if (!DateTime.TryParse(parts[3], out simTime))
                        continue;

                    string details = parts.Length > 4 ? parts[4] : "";

                    switch (eventType)
                    {
                        case "TrainEvent":
                            // Determine if incoming or outbound
                            if (eventName == "Entry" || eventName == "ArrivedArrivalTrack" || eventName == "PreparationRequested" ||
                                eventName == "PushOffStarted" || eventName == "ExitedSystem" || eventName == "AssignedArrivalTrack" ||
                                eventName == "ClassificationTrackAssigned" || eventName == "PreparationStarted" ||
                                eventName == "PreparationComplete" || eventName == "PushOffRequested" || eventName == "PushOffComplete")
                            {
                                if (!incomingTrains.ContainsKey(entityId))
                                    incomingTrains[entityId] = new IncomingTrainJourney { TrainId = entityId };
                                incomingTrains[entityId].AddEvent(eventName, simTime);
                            }
                            else if (eventName == "OutboundTrainCreated" || eventName == "TrainComplete" ||
                                     eventName == "LocomotiveRequested" || eventName == "LocoCouplingStarted" ||
                                     eventName == "LocomotiveCoupled" || eventName == "LeavingPrepRequested" ||
                                     eventName == "LeavingPrepStarted" || eventName == "LeavingPrepComplete" ||
                                     eventName == "DepartureProcessing" || eventName == "DepartureStarted" || eventName == "Departed")
                            {
                                if (!outboundTrains.ContainsKey(entityId))
                                    outboundTrains[entityId] = new OutboundTrainJourney { TrainId = entityId };
                                outboundTrains[entityId].AddEvent(eventName, simTime, details);
                            }
                            break;

                        case "WagonGroupEvent":
                            if (!wagonGroups.ContainsKey(entityId))
                                wagonGroups[entityId] = new WagonGroupJourney { WagonGroupId = entityId };
                            wagonGroups[entityId].AddEvent(eventName, simTime, details);
                            break;

                        case "WorkerEvent":
                            string workerKey = $"{entityId}_{details}"; // Combine worker ID + activity ID for uniqueness
                            if (!workerActivities.ContainsKey(workerKey))
                                workerActivities[workerKey] = new WorkerActivity { WorkerId = entityId, ActivityId = details };
                            workerActivities[workerKey].AddEvent(eventName, simTime, details);
                            break;

                        case "ActivityEvent":
                            // Can be used for additional context if needed
                            break;
                    }
                }
            }

            return (
                incomingTrains.Values.ToList(),
                wagonGroups.Values.ToList(),
                outboundTrains.Values.ToList(),
                workerActivities.Values.ToList(),
                locoActivities.Values.ToList()
            );
        }

        // Data classes
        private class IncomingTrainJourney
        {
            public string TrainId { get; set; }
            private Dictionary<string, DateTime> _events = new Dictionary<string, DateTime>();

            public void AddEvent(string eventName, DateTime time)
            {
                _events[eventName] = time;
            }

            public DateTime? GetEventTime(string eventName)
            {
                return _events.ContainsKey(eventName) ? _events[eventName] : (DateTime?)null;
            }
        }

        private class WagonGroupJourney
        {
            public string WagonGroupId { get; set; }
            public string ParentTrainId { get; set; }
            public string Destination { get; set; }
            private Dictionary<string, DateTime> _events = new Dictionary<string, DateTime>();

            public void AddEvent(string eventName, DateTime time, string details)
            {
                _events[eventName] = time;
                if (eventName == "ArrivedClassificationTrack" && !string.IsNullOrEmpty(details))
                    Destination = details;
            }

            public DateTime? GetEventTime(string eventName)
            {
                return _events.ContainsKey(eventName) ? _events[eventName] : (DateTime?)null;
            }
        }

        private class OutboundTrainJourney
        {
            public string TrainId { get; set; }
            public string Destination { get; set; }
            public string TotalLength { get; set; }
            public string WagonGroupCount { get; set; }
            private Dictionary<string, DateTime> _events = new Dictionary<string, DateTime>();

            public void AddEvent(string eventName, DateTime time, string details)
            {
                _events[eventName] = time;
                if (eventName == "OutboundTrainCreated" && !string.IsNullOrEmpty(details))
                    Destination = details;
                if (eventName == "Departed" && !string.IsNullOrEmpty(details))
                {
                    var parts = details.Split(' ');
                    if (parts.Length > 0)
                        TotalLength = parts[0];
                }
            }

            public DateTime? GetEventTime(string eventName)
            {
                return _events.ContainsKey(eventName) ? _events[eventName] : (DateTime?)null;
            }
        }

        private class WorkerActivity
        {
            public string WorkerId { get; set; }
            public string ActivityId { get; set; }
            public string Location { get; set; }
            public DateTime? AllocatedAt { get; set; }
            public DateTime? ArrivedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ReturnedAt { get; set; }

            public void AddEvent(string eventName, DateTime time, string details)
            {
                switch (eventName)
                {
                    case "Allocated":
                        AllocatedAt = time;
                        ActivityId = details;
                        break;
                    case "Arrived":
                        ArrivedAt = time;
                        ActivityId = details;
                        break;
                    case "Returned":
                        ReturnedAt = time;
                        break;
                }
            }
        }

        private class LocomotiveActivity
        {
            public string LocomotiveId { get; set; }
            public string ActivityId { get; set; }
            public string Location { get; set; }
            public DateTime? AllocatedAt { get; set; }
            public DateTime? ArrivedAt { get; set; }
            public DateTime? CompletedAt { get; set; }
            public DateTime? ReturnedAt { get; set; }

            public void AddEvent(string eventName, DateTime time, string details)
            {
                switch (eventName)
                {
                    case "Allocated":
                        AllocatedAt = time;
                        ActivityId = details;
                        break;
                    case "Arrived":
                        ArrivedAt = time;
                        ActivityId = details;
                        break;
                    case "Returned":
                        ReturnedAt = time;
                        break;
                }
            }
        }
    }
}