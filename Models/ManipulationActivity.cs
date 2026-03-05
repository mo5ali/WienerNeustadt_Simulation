using System;

namespace WienerNeustadtSimulation.Models
{
    public class ManipulationActivity : Activity
    {
        public override int RequiredWorkers => GetRequiredWorkersForType(ActivityType);
        public override bool RequiresLocomotive => GetRequiresLocomotiveForType(ActivityType);
        public override double BaseSecondsPerMeter => GetBaseSecondsPerMeterForType(ActivityType);

        public ManipulationActivity(
            string activityType,
            string entityId,
            double entityLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            bool autoSubmit = true)
            : base(activityType, entityId, entityLength, location, area, controlUnit, requestedAt, autoSubmit)
        {
        }

        private int GetRequiredWorkersForType(string type)
        {
            return type switch
            {
                "IncomingTrainPreparation" => 3,
                "Uncoupling" => 2,
                "Coupling" => 2,
                "Securing" => 1,
                "PushOff" => 3,
                _ => 2
            };
        }

        private bool GetRequiresLocomotiveForType(string type)
        {
            return type switch
            {
                "PushOff" => false,
                "Coupling" => false,
                "Uncoupling" => false,
                "Securing" => false,
                "IncomingTrainPreparation" => false,
                _ => false
            };
        }

        private double GetBaseSecondsPerMeterForType(string type)
        {
            return type switch
            {
                "IncomingTrainPreparation" => 15.0,
                "Uncoupling" => 10.0,
                "Coupling" => 12.0,
                "Securing" => 8.0,
                "PushOff" => 10.0,
                _ => 10.0
            };
        }
    }
}