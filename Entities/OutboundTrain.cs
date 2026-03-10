using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Entities
{
    public class OutboundTrain
    {
        public string Id { get; set; }
        public List<WagonGroup> WagonGroups { get; set; }
        public string Destination { get; set; }

        // Track location
        public string CurrentTrackId { get; set; }
        public string CurrentArea { get; set; }

        // State
        public bool HasLocomotive { get; set; }
        public string? LocomotiveId { get; set; }
        public bool LeavingPreparationComplete { get; set; }
        public DateTime? ScheduledDepartureTime { get; set; }

        // Calculated properties
        public double TotalLength => WagonGroups.Sum(wg => wg.Length);
        public int TotalWagonCount => WagonGroups.Sum(wg => wg.WagonIds.Count);

        public OutboundTrain(string destination, string trackId, string area)
        {
            Id = $"OBT-{destination}-{DateTime.Now:HHmmss}";
            Destination = destination;
            CurrentTrackId = trackId;
            CurrentArea = area;
            WagonGroups = new List<WagonGroup>();
            HasLocomotive = false;
            LeavingPreparationComplete = false;
        }

        public void AddWagonGroup(WagonGroup wagonGroup)
        {
            WagonGroups.Add(wagonGroup);
            wagonGroup.ParentTrain = this;
            wagonGroup.IsCoupled = true;
        }

        public override string ToString()
        {
            return $"{Id} ({TotalLength:F1}m, {WagonGroups.Count} WGs, {TotalWagonCount} wagons)";
        }
    }
}