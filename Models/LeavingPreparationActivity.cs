using System;

namespace WienerNeustadtSimulation.Models
{
    public class LeavingPreparationActivity : Activity
    {
        public override int RequiredWorkers => 2;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0; // Fixed time, not length-based

        public string TrainId { get; set; }
        public TimeSpan FixedDuration { get; set; }

        public LeavingPreparationActivity(
            string trainId,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            TimeSpan fixedDuration)
            : base("LeavingPreparation", trainId, 0, location, area, controlUnit, requestedAt)
        {
            TrainId = trainId;
            FixedDuration = fixedDuration;
        }

        public TimeSpan CalculateFixedDuration()
        {
            return FixedDuration;
        }

        protected override string GetActivityAbbreviation(string activityType)
        {
            return "LVP";
        }
    }
}