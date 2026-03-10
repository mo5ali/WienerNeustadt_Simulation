using System;

namespace WienerNeustadtSimulation.Models
{
    public class ResourceRequest
    {
        public string RequestId { get; set; }
        public Activity Activity { get; set; }
        public int RequiredWorkers { get; set; }
        public bool RequiresLocomotive { get; set; }
        public DateTime RequestedAt { get; set; }
        public string RequestingControlUnit { get; set; }

        public ResourceRequest(Activity activity, int workers, bool loco, DateTime time, string controlUnit)
        {
            Activity = activity;
            RequiredWorkers = workers;
            RequiresLocomotive = loco;
            RequestedAt = time;
            RequestingControlUnit = controlUnit;
            RequestId = $"Req_{activity.ActivityId}";
        }
    }
}