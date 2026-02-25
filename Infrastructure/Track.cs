namespace WienerNeustadtSimulation.Infrastructure
{
    public class Track
    {
        public string RealLifeID { get; set; }
        public string StationID { get; }
        public string Area { get; set; }
        public string Designation { get; set; }
        public double Length { get; set; }
        public List<string> SegmentIds { get; set; }
        public List<string> CurrentOccupancies { get; set; }
        public bool Reserved { get; set; }  // ← NEW: defaults to false

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
            Reserved = false;  // ← NEW: starts as false
            RealLifeID = string.Empty;
            Area = string.Empty;
            Designation = string.Empty;
            SegmentIds = new List<string>();
            CurrentOccupancies = new List<string>();
        }

        public void CalculateLength(Dictionary<string, Segment> segmentLookup, Dictionary<string, Node> nodeLookup)
        {
            if (SegmentIds == null || SegmentIds.Count == 0)
            {
                Length = 0;
                return;
            }

            Length = 0;
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