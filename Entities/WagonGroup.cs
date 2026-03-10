using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Entities
{
    public class WagonGroup
    {
        public string Id { get; set; }
        public double Length { get; set; }
        public string Destination { get; set; }
        public List<string> WagonIds { get; set; }

        // Track location
        public string CurrentTrackId { get; set; }
        public string CurrentArea { get; set; }

        // State
        public bool IsSecured { get; set; }
        public bool IsCoupled { get; set; }
        public DateTime ArrivedAt { get; set; }

        // Reference to parent train (when coupled into a train)
        public OutboundTrain? ParentTrain { get; set; }

        public WagonGroup(string id, double length, string destination, List<string> wagonIds)
        {
            Id = id;
            Length = length;
            Destination = destination;
            WagonIds = wagonIds ?? new List<string>();
            IsSecured = false;
            IsCoupled = false;
        }

        public override string ToString()
        {
            return $"WG-{Id} ({Length:F1}m, {WagonIds.Count} wagons → {Destination})";
        }
    }
}