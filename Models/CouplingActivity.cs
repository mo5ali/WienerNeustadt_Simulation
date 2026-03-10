using System;

namespace WienerNeustadtSimulation.Models
{
    public class CouplingActivity : Activity
    {
        public override int RequiredWorkers => 2;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 12.0;

        public string WagonGroupId { get; set; }
        public string CouplingToTrainId { get; set; }

        public CouplingActivity(
            string wagonGroupId,
            double wagonGroupLength,
            string couplingToTrainId,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base("Coupling", wagonGroupId, wagonGroupLength, location, area, controlUnit, requestedAt)
        {
            WagonGroupId = wagonGroupId;
            CouplingToTrainId = couplingToTrainId;
        }

        protected override string GetActivityAbbreviation(string activityType)
        {
            return "COP";
        }
    }
}