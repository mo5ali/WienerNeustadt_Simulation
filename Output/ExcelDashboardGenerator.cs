using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;

namespace WienerNeustadtSimulation.Output
{
    public class ExcelDashboardGenerator
    {
        public void GenerateFromLog(string logPath, string outputPath)
        {
            var (incomingTrains, wagonGroups, outboundTrains, workers, locomotives) = ParseLog(logPath);

            var templatePath = Path.Combine(AppContext.BaseDirectory, "Output", "Excel_Dashboard_Template.xlsx");
            FileInfo templateFile = new FileInfo(templatePath);
            FileInfo outputFile = new FileInfo(outputPath);

            if (!templateFile.Exists)
            {
                throw new FileNotFoundException($"❌ Template file not found: {templatePath}");
            }

            // Load template
            using (var package = new ExcelPackage(templateFile))
            {
                // Get sheets (they should already exist in template with headers formatted)
                var incomingSheet = package.Workbook.Worksheets["Incoming Trains"];
                var wagonGroupSheet = package.Workbook.Worksheets["Wagon Groups"];
                var outboundSheet = package.Workbook.Worksheets["Outbound Trains"];
                var workerSheet = package.Workbook.Worksheets["Workers"];
                var locoSheet = package.Workbook.Worksheets["Shunting Locomotives"];

                // Verify sheets exist
                if (incomingSheet == null) throw new Exception("Sheet 'Incoming Trains' not found in template");
                if (wagonGroupSheet == null) throw new Exception("Sheet 'Wagon Groups' not found in template");
                if (outboundSheet == null) throw new Exception("Sheet 'Outbound Trains' not found in template");
                if (workerSheet == null) throw new Exception("Sheet 'Workers' not found in template");
                if (locoSheet == null) throw new Exception("Sheet 'Shunting Locomotives' not found in template");

                // Clear old data (keep row 1 as headers)
                ClearDataRows(incomingSheet);
                ClearDataRows(wagonGroupSheet);
                ClearDataRows(outboundSheet);
                ClearDataRows(workerSheet);
                ClearDataRows(locoSheet);

                // Fill with new data (starting at row 2)
                FillIncomingTrainsData(incomingSheet, incomingTrains);
                FillWagonGroupsData(wagonGroupSheet, wagonGroups);
                FillOutboundTrainsData(outboundSheet, outboundTrains);
                FillWorkersData(workerSheet, workers);
                FillLocomotivesData(locoSheet, locomotives);

                // Save to output
                package.SaveAs(outputFile);
            }

            Console.WriteLine($"📊 Excel dashboard generated: {outputPath}");
            Console.WriteLine($"   ✓ {incomingTrains.Count} incoming trains");
            Console.WriteLine($"   ✓ {wagonGroups.Count} wagon groups");
            Console.WriteLine($"   ✓ {outboundTrains.Count} outbound trains");
            Console.WriteLine($"   ✓ {workers.Count} worker activities");
            Console.WriteLine($"   ✓ {locomotives.Count} locomotive activities");
        }

        private void ClearDataRows(ExcelWorksheet sheet)
        {
            if (sheet.Dimension == null) return;

            int lastRow = sheet.Dimension.End.Row;
            if (lastRow > 1)
            {
                sheet.DeleteRow(2, lastRow - 1);
            }
        }

        private void FillIncomingTrainsData(ExcelWorksheet worksheet, List<IncomingTrainJourney> trains)
        {
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
        }

        private void FillWagonGroupsData(ExcelWorksheet worksheet, List<WagonGroupJourney> wagonGroups)
        {
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
        }

        private void FillOutboundTrainsData(ExcelWorksheet worksheet, List<OutboundTrainJourney> trains)
        {
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
        }

        private void FillWorkersData(ExcelWorksheet worksheet, List<WorkerActivity> workers)
        {
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
        }

        private void FillLocomotivesData(ExcelWorksheet worksheet, List<LocomotiveActivity> locomotives)
        {
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
                            // Extract worker ID from event (format might be "workerId" in entityId)
                            if (eventName == "Allocated" && !string.IsNullOrEmpty(details))
                            {
                                string workerKey = $"{entityId}_{details}";
                                if (!workerActivities.ContainsKey(workerKey))
                                    workerActivities[workerKey] = new WorkerActivity { WorkerId = entityId, ActivityId = details };
                                workerActivities[workerKey].AddEvent(eventName, simTime, details);
                            }
                            else
                            {
                                // For Arrived/Returned events, match to existing activity
                                var matchingActivity = workerActivities.Values.FirstOrDefault(w => w.WorkerId == entityId && !w.ReturnedAt.HasValue);
                                if (matchingActivity != null)
                                {
                                    matchingActivity.AddEvent(eventName, simTime, details);
                                }
                            }
                            break;

                        case "ActivityEvent":
                            // Can be used for additional context if needed
                            break;
                    }
                }
            }

            // Separate locomotives from workers
            foreach (var worker in workerActivities.Values.ToList())
            {
                if (worker.WorkerId.StartsWith("SL") || worker.WorkerId.Contains("Loco"))
                {
                    string locoKey = $"{worker.WorkerId}_{worker.ActivityId}";
                    if (!locoActivities.ContainsKey(locoKey))
                    {
                        locoActivities[locoKey] = new LocomotiveActivity
                        {
                            LocomotiveId = worker.WorkerId,
                            ActivityId = worker.ActivityId,
                            Location = worker.Location,
                            AllocatedAt = worker.AllocatedAt,
                            ArrivedAt = worker.ArrivedAt,
                            CompletedAt = worker.CompletedAt,
                            ReturnedAt = worker.ReturnedAt
                        };
                    }
                    workerActivities.Remove($"{worker.WorkerId}_{worker.ActivityId}");
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
                        if (!string.IsNullOrEmpty(details))
                            ActivityId = details;
                        break;
                    case "Arrived":
                        ArrivedAt = time;
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
                        if (!string.IsNullOrEmpty(details))
                            ActivityId = details;
                        break;
                    case "Arrived":
                        ArrivedAt = time;
                        break;
                    case "Returned":
                        ReturnedAt = time;
                        break;
                }
            }
        }
    }
}