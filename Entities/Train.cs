using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Entities
{
    public class Train // Represents a train composed of wagon groups.
    {
        public string ID { get; } // 5-digit train identifier
        public double Length { get; set; } // Total length in meters (from file or calculated)
        public List<string> WagonGroupIds { get; set; } // List of wagon group IDs in this train
        public bool HasLoco { get; set; } // True if a locomotive is attached
        public string LocomotiveId { get; set; } // Locomotive ID (if HasLoco is true)
        public string Status { get; set; } // Current status (can be empty for now)
        public string Designation { get; set; } // e.g., "Inbound", "Outbound", "Passing"
        public DateTime Time { get; set; } // timestamp associated with this train (e.g. arrival time).

        /// <param name="id">5-digit train ID</param>
        /// <param name="length">Length in meters (optional if calculated later)</param>
        public Train(string id, double length = 0) // Constructor for Train.
        {
            // Validate ID is exactly 5 digits
            if (string.IsNullOrWhiteSpace(id) || id.Length != 5 || !long.TryParse(id, out _))
            {
                throw new ArgumentException("Train ID must be exactly 5 digits.", nameof(id));
            }

            ID = id;
            Length = length;
            WagonGroupIds = new List<string>(); // Initialize empty list of wagon group IDs
            HasLoco = false; // Default: no locomotive
            LocomotiveId = string.Empty; // Default: empty
            Status = string.Empty; // Default: empty
            Designation = string.Empty; // Default: empty

            // Default Time to MinValue (unset). Set this later when scheduling or parsing inbound data.
            Time = DateTime.MinValue;
        }

        // Method to calculate total length from wagon group objects (call after resolving IDs)
        public void CalculateLength(Dictionary<string, WagonGroup> wagonGroupLookup)
        {
            if (WagonGroupIds == null || WagonGroupIds.Count == 0)
            {
                Length = 0;
                return;
            }

            Length = WagonGroupIds.Sum(id => wagonGroupLookup[id].Length);
        }

        public override string ToString()
        {
            string locoInfo = HasLoco ? $", Loco: {LocomotiveId}" : ", No Loco";
            string designationInfo = !string.IsNullOrEmpty(Designation) ? $", {Designation}" : "";
            string statusInfo = !string.IsNullOrEmpty(Status) ? $", Status: {Status}" : "";
            string timeInfo = Time != DateTime.MinValue ? $", Time: {Time:yyyy-MM-ddTHH:mm:ssZ}" : "";

            return $"Train {ID} - {Length}m, {WagonGroupIds.Count} groups{locoInfo}{designationInfo}{statusInfo}{timeInfo}";
        }
    }
}