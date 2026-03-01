using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Models
{
    public class ResourceRequest
    {
        public string RequestId { get; set; }
        public string ForActivity { get; set; }

        /// <summary>
        /// Activity type key used to match worker skills, e.g. "IncomingTrainPreparation", "Coupling", "Securing".
        /// </summary>
        public string ActivityTypeKey { get; set; }

        public string TrackId { get; set; }
        public string Area { get; set; }

        /// <summary>
        /// For duration calculation at commencement.
        /// Example: for TrainPreparation -> train length (meters).
        /// </summary>
        public double EntityLengthMeters { get; set; }

        /// <summary>
        /// Base activity time in seconds per meter.
        /// FinalDurationSeconds = EntityLengthMeters * BaseSecondsPerMeter * AvgWorkerTimeMultiplier
        /// </summary>
        public double BaseSecondsPerMeter { get; set; }

        // What resources are required
        public int RequiredWorkers { get; set; }
        public int RequiredLocomotives { get; set; } // not used yet (workers-only implementation)

        // Resource allocation tracking (who got assigned)
        public List<string> AllocatedWorkerIds { get; set; } = new List<string>();
        public List<string> AllocatedLocoIds { get; set; } = new List<string>();

        // Travel/arrival tracking
        public HashSet<string> ArrivedWorkerIds { get; set; } = new HashSet<string>();

        // Timing
        public DateTime? SubmittedAtUtc { get; set; }
        public DateTime? CommencedAtUtc { get; set; }
        public DateTime? FinishedAtUtc { get; set; }

        /// <summary>
        /// Called when ALL required resources have arrived and the activity can commence.
        /// </summary>
        public Action<ResourceRequest>? OnFulfilled { get; set; }

        public ResourceRequest(
            string requestId,
            string forActivity,
            string activityTypeKey,
            string trackId,
            string area)
        {
            RequestId = requestId;
            ForActivity = forActivity;
            ActivityTypeKey = activityTypeKey;
            TrackId = trackId;
            Area = area;
        }
    }
}