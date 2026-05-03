using System;

namespace WienerNeustadtSimulation.Models
{
    public class ManipulationActivity : Activity
    {
        public override int RequiredWorkers => GetRequiredWorkersForType(ActivityType);
        public override bool RequiresLocomotive => GetRequiresLocomotiveForType(ActivityType);
        public override double BaseSecondsPerMeter => GetBaseSecondsPerMeterForType(ActivityType);

        // IncomingTrainPreparation allocates the loco that then stays for the PushOff.
        // All other manipulation activities have no loco.
        public override bool LocoStaysWithEntity => ActivityType == "IncomingTrainPreparation";

        // Workers are always returned as a batch for manipulation activities.
        public override bool WorkersReleasedIndividually => false;

        public ManipulationActivity(
            string activityType,
            string entityId,
            double entityLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base(activityType, entityId, entityLength, location, area, controlUnit, requestedAt)
        {
        }

        private int GetRequiredWorkersForType(string type)
        {
            return type switch
            {
                "IncomingTrainPreparation" => 3,
                "Uncoupling" => 2,
                "Coupling" => 1,
                "Securing" => 1,
                "PushOff" => 3,
                _ => 2
            };
        }

        private bool GetRequiresLocomotiveForType(string type)
        {
            return type switch
            {
                "IncomingTrainPreparation" => true,
                _ => false
            };
        }

        private double GetBaseSecondsPerMeterForType(string type)
        {
            return type switch
            {
                "IncomingTrainPreparation" => 7,
                "Uncoupling" => 10.0,
                "Coupling" => 12.0,
                "Securing" => 8.0,
                "PushOff" => 10.0,
                _ => 10.0
            };
        }
    }
}