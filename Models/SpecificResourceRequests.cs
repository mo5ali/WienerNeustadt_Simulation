using System;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Request for incoming train preparation resources
    /// </summary>
    public class TrainPreparationRequest : ResourceRequest
    {
        public double TrainLength { get; set; }
        public int UncouplingPoints { get; set; }

        public TrainPreparationRequest(
            string trainId,
            string locationTrackId,
            double trainLength,
            int uncouplingPoints,
            string locationArea = "")
            : base("ArrivalControlUnit", trainId, ActivityTypes.IncomingTrainPreparation, locationTrackId, locationArea)
        {
            TrainLength = trainLength;
            UncouplingPoints = uncouplingPoints;

            // Define required resources for train preparation
            AddResource(ResourceType.Worker, 4);
            AddResource(ResourceType.Supervisor, 1);
            AddResource(ResourceType.ShuntingLocomotive, 1);
        }
    }

    /// <summary>
    /// Request for push-off resources
    /// </summary>
    public class PushOffRequest : ResourceRequest
    {
        public int WagonGroupCount { get; set; }

        public PushOffRequest(
            string trainId,
            string locationTrackId,
            int wagonGroupCount,
            string locationArea = "")
            : base("ArrivalControlUnit", trainId, ActivityTypes.PushOff, locationTrackId, locationArea)
        {
            WagonGroupCount = wagonGroupCount;

            // Define required resources for push-off
            AddResource(ResourceType.ShuntingLocomotive, 1);
            AddResource(ResourceType.Worker, 2);
        }
    }

    /// <summary>
    /// Request for securing wagon group resources
    /// </summary>
    public class SecuringRequest : ResourceRequest
    {
        public SecuringRequest(
            string wagonGroupId,
            string locationTrackId,
            string locationArea = "")
            : base("ClassificationControlUnit", wagonGroupId, ActivityTypes.Securing, locationTrackId, locationArea)
        {
            // Define required resources for securing
            AddResource(ResourceType.Worker, 1);
            AddResource(ResourceType.SecuringEquipment, 1);
        }
    }

    /// <summary>
    /// Request for coupling wagon groups
    /// </summary>
    public class CouplingRequest : ResourceRequest
    {
        public string BackmostWagonGroupId { get; set; }

        public CouplingRequest(
            string wagonGroupId,
            string backmostWagonGroupId,
            string locationTrackId,
            string locationArea = "")
            : base("ClassificationControlUnit", wagonGroupId, ActivityTypes.Coupling, locationTrackId, locationArea)
        {
            BackmostWagonGroupId = backmostWagonGroupId;

            // Define required resources for coupling
            AddResource(ResourceType.Worker, 2);
            AddResource(ResourceType.CouplingEquipment, 1);
        }
    }

    /// <summary>
    /// Request for departure preparation resources
    /// </summary>
    public class DeparturePreparationRequest : ResourceRequest
    {
        public string OutboundTrainId { get; set; }
        public double TrainLength { get; set; }

        public DeparturePreparationRequest(
            string outboundTrainId,
            string locationTrackId,
            double trainLength,
            string locationArea = "")
            : base("DepartureControlUnit", outboundTrainId, ActivityTypes.DeparturePreparation, locationTrackId, locationArea)
        {
            OutboundTrainId = outboundTrainId;
            TrainLength = trainLength;

            // Define required resources for departure preparation
            AddResource(ResourceType.Worker, 3);
            AddResource(ResourceType.Supervisor, 1);
            AddResource(ResourceType.InspectionEquipment, 1);
            AddResource(ResourceType.ShuntingLocomotive, 1);
        }
    }

    /// <summary>
    /// Request for inspection resources
    /// </summary>
    public class InspectionRequest : ResourceRequest
    {
        public InspectionRequest(
            string entityId,
            string locationTrackId,
            string locationArea = "")
            : base("ClassificationControlUnit", entityId, ActivityTypes.Inspection, locationTrackId, locationArea)
        {
            // Define required resources for inspection
            AddResource(ResourceType.Worker, 1);
            AddResource(ResourceType.InspectionEquipment, 1);
        }
    }
}