namespace WienerNeustadtSimulation.Entities
{
    public class WagonGroup // Represents a group of wagons within a train.
    {
        public string ID { get; } // 7-digit identifier (5: Train ID, 2: Wagon Group ID)
        public double Length { get; set; } // Total length in meters (from file or calculated)
        public List<string> WagonIds { get; set; } // List of wagon IDs in this group
        public string Destination { get; set; } // e.g., "Vienna", "Graz"

        // Computed properties from ID
        public string ParentTrainId => ID.Substring(0, 5);
        public string GroupNumber => ID.Substring(5, 2);

        /// <param name="id">7-digit wagon group ID</param>
        /// <param name="length">Length in meters (optional if calculated later)</param>
        public WagonGroup(string id, double length = 0, string destination = "") // Constructor for WagonGroup.
        {

            if (string.IsNullOrWhiteSpace(id) || id.Length != 7 || !long.TryParse(id, out _)) // Validate ID is 7 digits
            {
                throw new ArgumentException("WagonGroup ID must be exactly 7 digits.", nameof(id));
            }

            ID = id;
            Length = length;
            WagonIds = new List<string>(); // Initialize empty list of IDs
            Destination = destination; // Initialize destination
        }

        // Method to calculate total length from wagon objects (call after resolving IDs)
        public void CalculateLength(Dictionary<string, Wagon> wagonLookup)
        {
            if (WagonIds == null || WagonIds.Count == 0)
            {
                Length = 0;
                return;
            }

            Length = WagonIds.Sum(id => wagonLookup[id].Length);
        }

        public override string ToString()
        {
            return $"WagonGroup {ID} (Train: {ParentTrainId}, Group#: {GroupNumber}) - {Length}m, {WagonIds.Count} wagons";
        }
    }
}