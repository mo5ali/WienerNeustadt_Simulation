using System;

namespace WienerNeustadtSimulation.Models
{
    public class LeavingPreparationActivity : Activity
    {
        public override int RequiredWorkers => 2;
        public override bool RequiresLocomotive => false; // Loco is already coupled to the train
        public override double BaseSecondsPerMeter => 0;  // Fixed time, not length-based

        // No loco to release.
        public override bool LocoStaysWithEntity => false;

        // Workers released individually so each is immediately re-dispatchable en-route
        // to the waiting area (brake test + paperwork is short, workers are needed fast).
        public override bool WorkersReleasedIndividually => true;

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

        public TimeSpan CalculateFixedDuration() => FixedDuration;

        protected override string GetActivityAbbreviation(string activityType) => "LVP";
    }
}