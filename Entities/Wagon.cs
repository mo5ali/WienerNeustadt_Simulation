namespace WienerNeustadtSimulation.Entities
{
    public class Wagon // Represents a single wagon.
    {
        public string ID { get; } // 9-digit wagon identifier. (5: Train ID, 3: Wagon Group ID, 2 digits: Wagon Number)
        public double Length { get; set; } // Length of the wagon in meters.

        // Computed properties from ID
        public string ParentTrainId => ID.Substring(0, 5);
        public string ParentWagonGroupId => ID.Substring(5, 3);
        public string WagonNumber => ID.Substring(7, 2);

        /// <param name="id">9-digit wagon ID</param>
        /// <param name="length">Length in meters</param>
        /// 
        public Wagon(string id, double length) // Constructor for Wagon.
        {
            if (string.IsNullOrWhiteSpace(id) || id.Length != 9 || !long.TryParse(id, out _)) // Validate ID is 9 digits
            {
                throw new ArgumentException("Wagon ID must be exactly 9 digits.", nameof(id));
            }

            if (length < 3 || length > 30) // Validate length is within acceptable range (3m to 30m)
            {
                throw new ArgumentException($"Wagon length must be between 3m and 30m.", nameof(length));
            }

            ID = id;
            Length = length;
        }

        public override string ToString()
        {
            return $"Wagon {ID} (Train: {ParentTrainId}, Group: {ParentWagonGroupId}, #: {WagonNumber}) - {Length}m";
        }
    }
}