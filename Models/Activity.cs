using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Models
{
    public abstract class Activity
    {
        public string ActivityId { get; set; }
        public string ActivityType { get; set; }
        public string EntityId { get; set; }
        public double EntityLength { get; set; }
        public string Location { get; set; }
        public string Area { get; set; }
        public string ControlUnit { get; set; }  // NEW: Track which CU initiated this

        // Timing
        public DateTime RequestedAt { get; set; }  // This is your "Initialization" time
        public DateTime? AllResourcesArrivedAt { get; set; }
        public DateTime? CommencedAt { get; set; }  // This is your "Commencement" time
        public DateTime? ScheduledCompletionAt { get; set; }
        public DateTime? CompletedAt { get; set; }  // This is your "Completion" time

        // Duration tracking
        public TimeSpan? CalculatedDuration { get; set; }
        public double? AverageWorkerMultiplier { get; set; }

        // Requirements (defined by subclass)
        public abstract int RequiredWorkers { get; }
        public abstract bool RequiresLocomotive { get; }
        public abstract double BaseSecondsPerMeter { get; }

        // Allocated resources
        public List<string> AllocatedWorkerIds { get; set; } = new List<string>();
        public List<string> AllocatedLocoIds { get; set; } = new List<string>();

        // Resource arrival tracking
        public Dictionary<string, DateTime> WorkerArrivalTimes { get; set; } = new Dictionary<string, DateTime>();
        public Dictionary<string, DateTime> LocoArrivalTimes { get; set; } = new Dictionary<string, DateTime>();

        // Callbacks
        public Action<Activity>? OnReadyToCommence { get; set; }
        public Action<Activity>? OnCompleted { get; set; }

        protected Activity(string activityType, string entityId, double entityLength, string location, string area, string controlUnit, DateTime requestedAt)
        {
            ActivityType = activityType;
            EntityId = entityId;
            EntityLength = entityLength;
            Location = location;
            Area = area;
            ControlUnit = controlUnit;
            RequestedAt = requestedAt;

            // Generate activity ID in the new format: Act_{Abbreviation}_{YYMMDDhhmmss}_{CU}_{EntityID}
            ActivityId = GenerateActivityId(activityType, requestedAt, controlUnit, entityId);

            // Auto-register in registry
            ActivityRegistry.Instance.Register(this);

            Console.WriteLine($"[Activity] {ActivityId} initialized at {RequestedAt:yyyy-MM-ddTHH:mm:ss}");
        }

        private string GenerateActivityId(string activityType, DateTime timestamp, string cu, string entityId)
        {
            string abbreviation = GetActivityAbbreviation(activityType);
            string timestampStr = timestamp.ToString("yyMMddHHmmss");
            return $"Act_{abbreviation}_{timestampStr}_{cu}_{entityId}";
        }

        protected virtual string GetActivityAbbreviation(string activityType)
        {
            // Default abbreviations - can be overridden in subclasses
            return activityType switch
            {
                "IncomingTrainPreparation" => "ITP",
                "Uncoupling" => "DEC",
                "Coupling" => "COP",
                "Securing" => "SEC",
                "PushOff" => "PO",
                "Entry" => "ENT",
                "Moving" => "MOV",
                "Leaving" => "LVG",
                _ => "ACT"
            };
        }

        // Calculate duration with worker modifiers
        public TimeSpan CalculateDuration(Dictionary<string, double> workerMultipliers)
        {
            // Get modifiers for allocated workers
            var modifiers = new List<double>();
            foreach (var workerId in AllocatedWorkerIds)
            {
                if (workerMultipliers.ContainsKey(workerId))
                    modifiers.Add(workerMultipliers[workerId]);
            }

            // Calculate average modifier
            AverageWorkerMultiplier = modifiers.Count > 0 ? modifiers.Average() : 1.0;

            // Duration = EntityLength × BaseSecondsPerMeter × AverageModifier
            double totalSeconds = EntityLength * BaseSecondsPerMeter * AverageWorkerMultiplier.Value;
            CalculatedDuration = TimeSpan.FromSeconds(totalSeconds);

            return CalculatedDuration.Value;
        }

        // Check if all required resources have arrived
        public bool AllResourcesArrived()
        {
            if (WorkerArrivalTimes.Count < RequiredWorkers)
                return false;

            if (RequiresLocomotive && LocoArrivalTimes.Count == 0)
                return false;

            return true;
        }

        // Record resource arrival
        public void RecordWorkerArrival(string workerId, DateTime arrivalTime)
        {
            WorkerArrivalTimes[workerId] = arrivalTime;
            Console.WriteLine($"{arrivalTime:dd/MM/yyyy-HH:mm:ss} | Activity {ActivityType}: worker {workerId} arrived at {Location}");

            CheckAndTriggerCommencement(arrivalTime);
        }

        public void RecordLocoArrival(string locoId, DateTime arrivalTime)
        {
            LocoArrivalTimes[locoId] = arrivalTime;
            Console.WriteLine($"{arrivalTime:dd/MM/yyyy-HH:mm:ss} | Activity {ActivityType}: locomotive {locoId} arrived at {Location}");

            CheckAndTriggerCommencement(arrivalTime);
        }

        // Check if all resources arrived and trigger commencement
        private void CheckAndTriggerCommencement(DateTime currentTime)
        {
            if (!AllResourcesArrived())
                return;

            if (AllResourcesArrivedAt == null)
            {
                AllResourcesArrivedAt = currentTime;
                Console.WriteLine($"{currentTime:dd/MM/yyyy-HH:mm:ss} | Activity {ActivityType}: ALL resources arrived, ready to commence");

                // Trigger callback - the CU will calculate duration and schedule completion
                OnReadyToCommence?.Invoke(this);
            }
        }
    }
}