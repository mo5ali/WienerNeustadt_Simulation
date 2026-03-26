using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Entities
{
    public class OutboundTrain
    {
        public string Id { get; set; }
        public string Destination { get; set; }
        public string CurrentTrackId { get; set; }
        public string CurrentArea { get; set; }
        public List<WagonGroup> WagonGroups { get; set; }
        public int TotalWagonCount { get; set; }
        public double TotalLength { get; set; }
        public string? LocomotiveId { get; set; }
        public bool LeavingPreparationComplete { get; set; }

        public bool HasLocomotive => !string.IsNullOrEmpty(LocomotiveId);

        public OutboundTrain(string trackId, string destination, List<WagonGroup> wagonGroups, DateTime createdAt)
        {
            // NEW FORMAT: OBT{ddmmyyhhmmss}-{Destination}
            Id = $"OBT{createdAt:ddMMyyHHmmss}-{destination}";
            Destination = destination;
            CurrentTrackId = trackId;
            CurrentArea = "Classification";
            WagonGroups = new List<WagonGroup>(wagonGroups);
            TotalWagonCount = wagonGroups.Sum(wg => wg.WagonIds.Count);  // FIX: Use WagonIds.Count
            TotalLength = wagonGroups.Sum(wg => wg.Length);
            LeavingPreparationComplete = false;
        }

        public void AddWagonGroup(WagonGroup wg)
        {
            WagonGroups.Add(wg);
            TotalWagonCount += wg.WagonIds.Count;  // FIX: Use WagonIds.Count
            TotalLength += wg.Length;
        }

        public override string ToString()
        {
            return $"{Id} ({TotalLength:F1}m, {WagonGroups.Count} WGs, dest: {Destination})";
        }
    }
}