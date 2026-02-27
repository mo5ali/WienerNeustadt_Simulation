using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Request for incoming train preparation activity.
    /// </summary>
    public class TrainPreparationRequest : ResourceRequest
    {
        public TrainPreparationRequest(string trainId, string trackId, string area)
            : base($"TrainPrep-{trainId}", "Train Preparation", trackId, area)
        {
            // Define what this activity needs (simple version for now)
            RequiredWorkers = 1;
            RequiredLocomotives = 0;
        }

        public int RequiredWorkers { get; set; }
        public int RequiredLocomotives { get; set; }
    }

    /// <summary>
    /// Request for push-off activity.
    /// </summary>
    public class PushOffRequest : ResourceRequest
    {
        public PushOffRequest(string trainId, string trackId, string area)
            : base($"PushOff-{trainId}", "Push Off", trackId, area)
        {
            RequiredWorkers = 1;
            RequiredLocomotives = 1; // Needs a shunting locomotive
        }

        public int RequiredWorkers { get; set; }
        public int RequiredLocomotives { get; set; }
    }

    /// <summary>
    /// Request for coupling activity.
    /// </summary>
    public class CouplingRequest : ResourceRequest
    {
        public CouplingRequest(string wagonGroupId, string trackId, string area)
            : base($"Coupling-{wagonGroupId}", "Coupling", trackId, area)
        {
            RequiredWorkers = 2;
            RequiredLocomotives = 0;
        }

        public int RequiredWorkers { get; set; }
        public int RequiredLocomotives { get; set; }
    }

    /// <summary>
    /// Request for securing activity.
    /// </summary>
    public class SecuringRequest : ResourceRequest
    {
        public SecuringRequest(string wagonGroupId, string trackId, string area)
            : base($"Securing-{wagonGroupId}", "Securing", trackId, area)
        {
            RequiredWorkers = 1;
            RequiredLocomotives = 0;
        }

        public int RequiredWorkers { get; set; }
        public int RequiredLocomotives { get; set; }
    }
}