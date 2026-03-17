using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OfficeOpenXml;
using OfficeOpenXml.Style;
using System.Drawing;
using System.ComponentModel;

namespace WienerNeustadtSimulation.Output
{
    public class ExcelDashboardGenerator
    {
        public void GenerateFromLog(string logPath, string outputPath)
        {

            var trainJourneys = ParseTrainJourneys(logPath);

            using (var package = new ExcelPackage())
            {
                var worksheet = package.Workbook.Worksheets.Add("Train Timeline");

                // Define columns
                var columns = new List<string>
                {
                    "Train ID",
                    "Train enters entry tracks (initialized)",
                    "Arrival track is requested",
                    "Arrival track is assigned to train",
                    "Train leaves the Entry track",
                    "Train arrives at Arrival track",
                    "Request for Incoming train preparation activity",
                    "Request for sorting activity",
                    "Sorting activity begins",
                    "Sorting activity ends",
                    "Incoming train preparation activity begins",
                    "Incoming train preparation activity ends",
                    "Push off process for incoming train begins"
                };

                // Write header row
                for (int i = 0; i < columns.Count; i++)
                {
                    worksheet.Cells[1, i + 1].Value = columns[i];
                }

                // Style header
                using (var range = worksheet.Cells[1, 1, 1, columns.Count])
                {
                    range.Style.Font.Bold = true;
                    range.Style.Fill.PatternType = ExcelFillStyle.Solid;
                    range.Style.Fill.BackgroundColor.SetColor(Color.FromArgb(68, 114, 196));
                    range.Style.Font.Color.SetColor(Color.White);
                    range.Style.HorizontalAlignment = ExcelHorizontalAlignment.Center;
                }

                // Write data rows
                int row = 2;
                foreach (var journey in trainJourneys.OrderBy(j => j.EntryTime))
                {
                    worksheet.Cells[row, 1].Value = journey.TrainId;

                    // Map events to columns
                    worksheet.Cells[row, 2].Value = FormatTimestamp(journey.GetEventTime("Entry"));
                    worksheet.Cells[row, 3].Value = FormatTimestamp(journey.GetEventTime("ArrivalTrackRequested"));
                    worksheet.Cells[row, 4].Value = FormatTimestamp(journey.GetEventTime("AssignedArrivalTrack"));
                    worksheet.Cells[row, 5].Value = FormatTimestamp(journey.GetEventTime("LeavingEntry"));
                    worksheet.Cells[row, 6].Value = FormatTimestamp(journey.GetEventTime("ArrivedArrivalTrack"));
                    worksheet.Cells[row, 7].Value = FormatTimestamp(journey.GetEventTime("PreparationRequested"));
                    worksheet.Cells[row, 8].Value = FormatTimestamp(journey.GetEventTime("ClassificationDetermined"));
                    worksheet.Cells[row, 9].Value = FormatTimestamp(journey.GetEventTime("SortingStarted"));
                    worksheet.Cells[row, 10].Value = FormatTimestamp(journey.GetEventTime("SortingComplete"));
                    worksheet.Cells[row, 11].Value = FormatTimestamp(journey.GetEventTime("PreparationStarted"));
                    worksheet.Cells[row, 12].Value = FormatTimestamp(journey.GetEventTime("PreparationComplete"));
                    worksheet.Cells[row, 13].Value = FormatTimestamp(journey.GetEventTime("PushOffStarted"));

                    row++;
                }

                // Auto-fit columns
                worksheet.Cells.AutoFitColumns();

                // Set minimum column width
                for (int col = 1; col <= columns.Count; col++)
                {
                    if (worksheet.Column(col).Width < 15)
                        worksheet.Column(col).Width = 15;
                }

                // Freeze header row
                worksheet.View.FreezePanes(2, 1);

                // Save file
                var fileInfo = new FileInfo(outputPath);
                package.SaveAs(fileInfo);
            }

            Console.WriteLine($"📊 Excel dashboard generated: {outputPath}");
        }

        private string FormatTimestamp(DateTime? timestamp)
        {
            if (!timestamp.HasValue)
                return "";

            return timestamp.Value.ToString("dd.MM.yyyy HH:mm:ss");
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

                        if (!journeys.ContainsKey(trainId))
                        {
                            journeys[trainId] = new TrainJourney { TrainId = trainId };
                        }

                        journeys[trainId].AddEvent(eventName, simTime);
                    }
                }
            }

            return journeys.Values.ToList();
        }

        private class TrainJourney
        {
            public string TrainId { get; set; }
            public DateTime? EntryTime { get; set; }
            private Dictionary<string, DateTime> _events = new Dictionary<string, DateTime>();

            public void AddEvent(string eventName, DateTime time)
            {
                _events[eventName] = time;

                if (eventName == "Entry")
                    EntryTime = time;
            }

            public DateTime? GetEventTime(string eventName)
            {
                return _events.ContainsKey(eventName) ? _events[eventName] : (DateTime?)null;
            }
        }
    }
}