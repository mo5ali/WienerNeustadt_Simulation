using WienerNeustadtSimulation.Infrastructure;

namespace WienerNeustadtSimulation.Models
{
    public sealed class TrainPreparationRequest : ResourceRequest
    {
        public TrainPreparationRequest(string trainId, string trackId, string area = "")
            : base("ArrivalControlUnit", trainId, ActivityTypes.IncomingTrainPreparation, trackId, area)
        {
            // Example from your requirement:
            AddResource(ResourceType.Worker, 4);
            AddResource(ResourceType.Supervisor, 1);
            AddResource(ResourceType.ShuntingLocomotive, 1);
        }
    }

    public sealed class PushOffRequest : ResourceRequest
    {
        public PushOffRequest(string trainId, string trackId, string area = "")
            : base("ArrivalControlUnit", trainId, ActivityTypes.PushOff, trackId, area)
        {
            AddResource(ResourceType.ShuntingLocomotive, 1);
            AddResource(ResourceType.Worker, 2);
        }
    }
}