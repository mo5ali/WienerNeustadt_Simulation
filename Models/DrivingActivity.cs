using System;

namespace WienerNeustadtSimulation.Models
{
    public class DrivingActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => true;
        public override double BaseSecondsPerMeter => 0;

        public double Speed { get; set; }

        public DrivingActivity(
            string activityType,
            string entityId,
            double entityLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            double speed,
            bool autoSubmit = false)  // Default false for sub-activities
            : base(activityType, entityId, entityLength, location, area, controlUnit, requestedAt, autoSubmit)
        {
            Speed = speed;
        }

        public TimeSpan CalculateDrivingDuration(double distanceMeters)
        {
            if (Speed <= 0)
                return TimeSpan.FromMinutes(1);

            double minutes = distanceMeters / Speed;
            return TimeSpan.FromMinutes(minutes);
        }
    }
}