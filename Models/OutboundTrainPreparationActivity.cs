using System;
using WienerNeustadtSimulation.Engine;

namespace WienerNeustadtSimulation.Models
{
    public class OutboundTrainPreparationActivity : Activity
    {
        public override int RequiredWorkers => 2;
        public override bool RequiresLocomotive => false;  // We'll request "Train Locomotive" separately
        public override double BaseSecondsPerMeter => 15.0;
        public override bool LocoStaysWithEntity => true;  // Train locomotive stays with the train
        public override bool WorkersReleasedIndividually => false;

        public bool RequiresTrainLocomotive => true;  // Custom flag for train locomotive

        public OutboundTrainPreparationActivity(
            SimulationEngine engine,  // FIX: Correct namespace
            string trainId,
            double trainLength,
            string trackId,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base(
                activityType: "OutboundTrainPreparation",
                entityId: trainId,
                entityLength: trainLength,
                location: trackId,
                area: area,
                controlUnit: controlUnit,
                requestedAt: requestedAt)
        {
        }
    }
}