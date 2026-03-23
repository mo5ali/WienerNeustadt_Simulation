using System;

namespace WienerNeustadtSimulation.Models
{
    public class SecuringActivity : Activity
    {
        public override int RequiredWorkers => 1;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 8.0;

        // No loco involved; single worker goes back to pool as a batch (same thing).
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

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

        protected override string GetActivityAbbreviation(string activityType) => "SEC";
    }
}