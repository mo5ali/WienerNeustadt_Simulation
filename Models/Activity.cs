using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Output;

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

        protected Activity(string activityType, string entityId, double entityLength, string location, string area, string controlUnit, DateTime requestedAt, string? extraDetails = null)
        {
            ActivityType = activityType;
            EntityId = entityId;
            EntityLength = entityLength;
            Location = location;
            Area = area;
            ControlUnit = controlUnit;
            RequestedAt = requestedAt;

            ActivityId = GenerateActivityId(activityType, requestedAt, controlUnit, entityId, location);

            // Don't print "initialized" for sub-activities or activities that already have a preview message
            if (activityType != "PushOffDrive" &&
                activityType != "Entry" &&
                activityType != "Moving" &&
                activityType != "Departure" &&
                activityType != "Securing" &&
                activityType != "Coupling" &&
                activityType != "OutboundTrainPreparation" &&  // NEW - printed in ClassifCU
                activityType != "DepartureDrive")               // NEW - printed in ClassifCU
            {
                Console.WriteLine($"{requestedAt:dd/MM/yyyy-HH:mm:ss.ff} | {controlUnit}: initialized {ActivityId}");
            }


            // Canonical activity lifecycle log: every activity emits Submitted at creation,
            // then Started via MarkCommenced() and Completed via MarkCompleted().
            // This is the single source of truth for "three timestamps per activity" in the log.
            // Subclasses may inject extra key=value pairs via the optional extraDetails
            // constructor argument — used by ITP to carry the separation-joint count.
            var details = $"entity={entityId};length={entityLength:F1};location={location};cu={controlUnit}";
            if (!string.IsNullOrEmpty(extraDetails))
                details += ";" + extraDetails;
            SimulationLogger.Instance.LogActivityEvent(
                ActivityId,
                ActivityType,
                requestedAt,
                status: "Submitted",
                details: details
            );
        }

        // Mark the activity as having commenced at `now`. Sets CommencedAt and emits
        // the ActivityEvent Started row. Call sites that used to do
        //   activity.CommencedAt = _engine.Now;
        // should now do
        //   activity.MarkCommenced(_engine.Now);
        public void MarkCommenced(DateTime now)
        {
            if (CommencedAt.HasValue) return; // idempotent guard
            CommencedAt = now;

            var workers = AllocatedWorkerIds != null && AllocatedWorkerIds.Count > 0
                ? string.Join(",", AllocatedWorkerIds)
                : "";
            var locos = AllocatedLocoIds != null && AllocatedLocoIds.Count > 0
                ? string.Join(",", AllocatedLocoIds)
                : "";
            var durMin = CalculatedDuration.HasValue
                ? CalculatedDuration.Value.TotalMinutes.ToString("F2")
                : "";
            var mult = AverageWorkerMultiplier.HasValue
                ? AverageWorkerMultiplier.Value.ToString("F2")
                : "";

            SimulationLogger.Instance.LogActivityEvent(
                ActivityId,
                ActivityType,
                now,
                status: "Started",
                details: $"workers={workers};locos={locos};durationMin={durMin};avgMult={mult}"
            );
        }

        // Mark the activity as complete. Sets CompletedAt and emits the ActivityEvent
        // Completed row. Idempotent — calling twice is a no-op on the second call.
        public void MarkCompleted(DateTime now)
        {
            if (CompletedAt.HasValue) return; // idempotent guard
            CompletedAt = now;

            var actualMin = (CommencedAt.HasValue
                ? (now - CommencedAt.Value).TotalMinutes
                : 0.0).ToString("F2");

            SimulationLogger.Instance.LogActivityEvent(
                ActivityId,
                ActivityType,
                now,
                status: "Completed",
                details: $"actualDurationMin={actualMin}"
            );
        }

        private string GenerateActivityId(string activityType, DateTime timestamp, string cu, string entityId, string location)
        {
            string abbreviation = GetActivityAbbreviation(activityType);
            string timestampStr = timestamp.ToString("HHmmss");
            // Keep the location token IDSafe — strip anything that might break downstream splits on '_'.
            string locToken = string.IsNullOrEmpty(location) ? "NA" : location.Replace('_', '-').Replace(' ', '-');
            return $"Act_{abbreviation}_{entityId}_{timestampStr}_{locToken}";
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
                "OutboundTrainPreparation" => "OBTP",  // NEW
                "DepartureDrive" => "DEPD",             // NEW
                "ArrivalDrive" => "ARRD",             // NEW
                "Departure" => "DEP",
                _ => "ACT"
            };
        }

        // Calculate duration with worker modifiers.
        // Virtual so subclasses can substitute a different formula —
        // see ManipulationActivity's override for IncomingTrainPreparation,
        // which uses the separation-joint-driven model instead of the
        // length × base-seconds-per-meter model used by every other activity.
        public virtual TimeSpan CalculateDuration(Dictionary<string, double> workerMultipliers)
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