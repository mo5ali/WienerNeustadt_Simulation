namespace WienerNeustadtSimulation.Infrastructure
{
    public class Track // Represents a track composed of multiple segments.
    {
        public string RealLifeID { get; set; } // Real-world track identifier (e.g., "Track-A1")
        public string StationID { get; } // 4-digit station identifier
        public string Area { get; set; } // Area/zone within the station
        public string Designation { get; set; } // e.g., "Entry", "Classification", "Departure"
        public double Length { get; set; } // Total length in meters (from file or calculated)
        public List<string> SegmentIds { get; set; } // List of segment IDs composing this track
        public List<string> CurrentOccupancies { get; set; } // List of entity IDs currently occupying this track

        /// <param name="stationId">4-digit station ID</param>
        /// <param name="length">Length in meters (optional if calculated later)</param>
        public Track(string stationId, double length = 0)
        {
            if (string.IsNullOrWhiteSpace(stationId) || stationId.Length != 4 || !long.TryParse(stationId, out _))
            {
                throw new ArgumentException("Station ID must be exactly 4 digits.", nameof(stationId));
            }

            if (length < 0)
            {
                throw new ArgumentException("Track length cannot be negative.", nameof(length));
            }

            StationID = stationId;
            Length = length;
            RealLifeID = string.Empty;
            Area = string.Empty;
            Designation = string.Empty;
            SegmentIds = new List<string>();
            CurrentOccupancies = new List<string>();
        }

        // Method to calculate total length from segment objects (implement later when you have segment length data)
        public void CalculateLength(Dictionary<string, Segment> segmentLookup, Dictionary<string, Node> nodeLookup)
        {
            if (SegmentIds == null || SegmentIds.Count == 0)
            {
                Length = 0;
                return;
            }

            // Calculate distance between start and end nodes for each segment
            // For now, this is a placeholder - you'll need to implement distance calculation
            // using node coordinates (Haversine formula or Euclidean distance)
            Length = 0; // TODO: Implement actual calculation
        }

        public override string ToString()
        {
            string realIdInfo = !string.IsNullOrEmpty(RealLifeID) ? $" ({RealLifeID})" : "";
            string designationInfo = !string.IsNullOrEmpty(Designation) ? $", {Designation}" : "";
            string occupancyInfo = CurrentOccupancies.Count > 0 ? $", {CurrentOccupancies.Count} occupants" : "";

            return $"Track {StationID}{realIdInfo} - {Length}m, {SegmentIds.Count} segments{designationInfo}{occupancyInfo}";
        }
    }
}