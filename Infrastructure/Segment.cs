namespace WienerNeustadtSimulation.Infrastructure
{
    public class Segment // Represents a straight line between two nodes.
    {
        public string ID { get; } // 6-digit identifier (first 4: parent track's StationID, last 2: segment number)
        public string StartNodeId { get; set; } // ID of the starting node
        public string EndNodeId { get; set; } // ID of the ending node

        // Computed property from ID
        public string ParentTrackStationId => ID.Substring(0, 4);
        public string SegmentNumber => ID.Substring(4, 2);

        /// <param name="id">6-digit segment ID</param>
        /// <param name="startNodeId">Starting node ID</param>
        /// <param name="endNodeId">Ending node ID</param>
        public Segment(string id, string startNodeId, string endNodeId)
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length != 6 || !long.TryParse(id, out _))
            {
                throw new ArgumentException("Segment ID must be exactly 6 digits.", nameof(id));
            }

            if (string.IsNullOrWhiteSpace(startNodeId))
            {
                throw new ArgumentException("Start node ID cannot be empty.", nameof(startNodeId));
            }

            if (string.IsNullOrWhiteSpace(endNodeId))
            {
                throw new ArgumentException("End node ID cannot be empty.", nameof(endNodeId));
            }

            ID = id;
            StartNodeId = startNodeId;
            EndNodeId = endNodeId;
        }

        public override string ToString()
        {
            return $"Segment {ID} (Track: {ParentTrackStationId}) from Node {StartNodeId} to Node {EndNodeId}";
        }
    }
}