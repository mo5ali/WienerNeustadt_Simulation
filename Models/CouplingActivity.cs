using System;

namespace WienerNeustadtSimulation.Models
{
    public class CouplingActivity : Activity
    {
        public override int RequiredWorkers => 2;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 12.0;

        // Set at construction time: true when coupling a locomotive to an outbound train
        // (loco stays with the train until departure); false for wagon group coupling.
        public override bool LocoStaysWithEntity { get; }

        // Workers are released individually so each is immediately dispatchable en-route
        // back to the waiting area.
        public override bool WorkersReleasedIndividually => true;

        public string WagonGroupId { get; set; }
        public string CouplingToTrainId { get; set; }

        public CouplingActivity(
            string wagonGroupId,
            double wagonGroupLength,
            string couplingToTrainId,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            bool locoStaysWithEntity = false)
            : base("Coupling", wagonGroupId, wagonGroupLength, location, area, controlUnit, requestedAt)
        {
            WagonGroupId = wagonGroupId;
            CouplingToTrainId = couplingToTrainId;
            LocoStaysWithEntity = locoStaysWithEntity;
        }

        protected override string GetActivityAbbreviation(string activityType) => "COP";
    }
}