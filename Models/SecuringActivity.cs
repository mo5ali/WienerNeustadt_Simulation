using System;

namespace WienerNeustadtSimulation.Models
{
    public class SecuringActivity : Activity
    {
        public override int RequiredWorkers => 1;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 8.0;

        public string WagonGroupId { get; set; }

        public SecuringActivity(
            string wagonGroupId,
            double wagonGroupLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base("Securing", wagonGroupId, wagonGroupLength, location, area, controlUnit, requestedAt)
        {
            WagonGroupId = wagonGroupId;
        }

        protected override string GetActivityAbbreviation(string activityType)
        {
            return "SEC";
        }
    }
}