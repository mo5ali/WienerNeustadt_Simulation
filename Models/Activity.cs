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
        public string ControlUnit { get; set; }

        // Timing
        public DateTime RequestedAt { get; set; }
        public DateTime? AllResourcesArrivedAt { get; set; }
        public DateTime? CommencedAt { get; set; }
        public DateTime? ScheduledCompletionAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        // Duration tracking
        public TimeSpan? CalculatedDuration { get; set; }
        public double? AverageWorkerMultiplier { get; set; }

        // Requirements (defined by subclass)
        public abstract int RequiredWorkers { get; }
        public abstract bool RequiresLocomotive { get; }
        public abstract double BaseSecondsPerMeter { get; }

        // Post-completion resource policy (defined by subclass)
        // true  ? loco remains coupled to the entity after this activity ends; ResourceCU must NOT return it
        // false ? loco is done with this activity and must be returned to the pool
        public abstract bool LocoStaysWithEntity { get; }

        // true  ? workers are returned individually (each triggers queue check; models en-route availability)
        // false ? workers are returned as a batch (one queue check at the end)
        public abstract bool WorkersReleasedIndividually { get; }

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

            ActivityId = GenerateActivityId(activityType, requestedAt, controlUnit, entityId);

            Console.WriteLine($"{requestedAt:dd/MM/yyyy-HH:mm:ss} | {controlUnit}: initialized {ActivityId}");

            ActivityRegistry.Instance.Register(this);
        }

        private string GenerateActivityId(string activityType, DateTime timestamp, string cu, string entityId)
        {
            string abbreviation = GetActivityAbbreviation(activityType);
            string timestampStr = timestamp.ToString("yyMMddHHmmss");
            return $"Act_{abbreviation}_{timestampStr}_{cu}_{entityId}";
        }

        protected virtual string GetActivityAbbreviation(string activityType)
        {
            return activityType switch
            {
                "IncomingTrainPreparation" => "ITP",
                "Uncoupling" => "DEC",
                "Coupling" => "COP",
                "Securing" => "SEC",
                "PushOff" => "PO",
                "PushOffDrive" => "POD",
                "Entry" => "ENT",
                "Moving" => "MOV",
                "Leaving" => "LVG",
                "LeavingPreparation" => "LVP",
                "Departure" => "DEP",
                _ => "ACT"
            };
        }

        // Calculate duration with worker modifiers
        public TimeSpan CalculateDuration(Dictionary<string, double> workerMultipliers)
        {
            var modifiers = new List<double>();
            foreach (var workerId in AllocatedWorkerIds)
            {
                if (workerMultipliers.ContainsKey(workerId))
                    modifiers.Add(workerMultipliers[workerId]);
            }

            AverageWorkerMultiplier = modifiers.Count > 0 ? modifiers.Average() : 1.0;

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
            CheckAndTriggerCommencement(arrivalTime);
        }

        public void RecordLocoArrival(string locoId, DateTime arrivalTime)
        {
            LocoArrivalTimes[locoId] = arrivalTime;
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
                OnReadyToCommence?.Invoke(this);
            }
        }
    }
}