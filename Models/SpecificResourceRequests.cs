namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Request for incoming train preparation activity.
    /// </summary>
    public class TrainPreparationRequest : ResourceRequest
    {
        public TrainPreparationRequest(string trainId, double trainLengthMeters, string trackId, string area)
            : base(
                  requestId: $"TrainPrep-{trainId}",
                  forActivity: $"TrainPreparation-{trainId}",
                  activityTypeKey: "IncomingTrainPreparation",
                  trackId: trackId,
                  area: area)
        {
            RequiredWorkers = 1;
            RequiredLocomotives = 0;

            EntityLengthMeters = trainLengthMeters;

            // Example base time (you can tune this):
            // 15 seconds per meter
            BaseSecondsPerMeter = 15.0;
        }
    }

    /// <summary>
    /// Request for push-off activity.
    /// </summary>
    public class PushOffRequest : ResourceRequest
    {
        public PushOffRequest(string trainId, double trainLengthMeters, string trackId, string area)
            : base(
                  requestId: $"PushOff-{trainId}",
                  forActivity: $"PushOff-{trainId}",
                  activityTypeKey: "PushOff",
                  trackId: trackId,
                  area: area)
        {
            RequiredWorkers = 1;
            RequiredLocomotives = 1; // TODO: implement loco travel/allocation too

            EntityLengthMeters = trainLengthMeters;
            BaseSecondsPerMeter = 10.0;
        }
    }

    /// <summary>
    /// Request for coupling activity.
    /// </summary>
    public class CouplingRequest : ResourceRequest
    {
        public CouplingRequest(string wagonGroupId, double wagonGroupLengthMeters, string trackId, string area)
            : base(
                  requestId: $"Coupling-{wagonGroupId}",
                  forActivity: $"Coupling-{wagonGroupId}",
                  activityTypeKey: "Coupling",
                  trackId: trackId,
                  area: area)
        {
            RequiredWorkers = 2;
            RequiredLocomotives = 0;

            EntityLengthMeters = wagonGroupLengthMeters;
            BaseSecondsPerMeter = 8.0;
        }
    }

    /// <summary>
    /// Request for securing activity.
    /// </summary>
    public class SecuringRequest : ResourceRequest
    {
        public SecuringRequest(string wagonGroupId, double wagonGroupLengthMeters, string trackId, string area)
            : base(
                  requestId: $"Securing-{wagonGroupId}",
                  forActivity: $"Securing-{wagonGroupId}",
                  activityTypeKey: "Securing",
                  trackId: trackId,
                  area: area)
        {
            RequiredWorkers = 1;
            RequiredLocomotives = 0;

            EntityLengthMeters = wagonGroupLengthMeters;
            BaseSecondsPerMeter = 6.0;
        }
    }
}