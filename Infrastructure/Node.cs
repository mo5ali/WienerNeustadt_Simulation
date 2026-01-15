namespace WienerNeustadtSimulation.Infrastructure
{
    using System;
    using System.Collections.Generic;

    public class Node // Represents a point in the track network (switch, bend, or gate).
    {
        public string ID { get; } // Node identifier (e.g., ConnectionPointId as string)
        public string RealLifeID { get; } // Original infrastructure "Id" (kept as string)
        public string Type { get; set; } // "Switch", "Bend", or "Gate"
        public double Latitude { get; set; } // Geographic latitude
        public double Longitude { get; set; } // Geographic longitude

        // Valid node types
        private static readonly HashSet<string> ValidTypes = new HashSet<string>
        {
            "Switch", "Bend", "Gate"
        };

        /// <param name="id">Node ID (ConnectionPointId)</param>
        /// <param name="realLifeId">Original infrastructure Id to store as RealLifeID</param>
        /// <param name="type">Node type: Switch, Bend, or Gate</param>
        /// <param name="latitude">Latitude coordinate</param>
        /// <param name="longitude">Longitude coordinate</param>
        public Node(string id, string realLifeId, string type, double latitude, double longitude)
        {
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new ArgumentException("Node ID cannot be empty.", nameof(id));
            }

            if (realLifeId == null)
            {
                // prefer explicit empty string over null to simplify JSON serialization and comparisons
                realLifeId = string.Empty;
            }

            if (!ValidTypes.Contains(type))
            {
                throw new ArgumentException($"Node type must be one of: {string.Join(", ", ValidTypes)}", nameof(type));
            }

            ID = id;
            RealLifeID = realLifeId;
            Type = type;
            Latitude = latitude;
            Longitude = longitude;
        }

        public override string ToString()
        {
            return $"Node {ID} (RealLifeID={RealLifeID}, {Type}) at ({Latitude}, {Longitude})";
        }
    }
}