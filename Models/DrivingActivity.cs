using System;

namespace WienerNeustadtSimulation.Models
{
    public class DrivingActivity : Activity
    {
        // Driving activities typically don't require workers or locomotives in the same way
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => true;  // Most driving needs a locomotive
        public override double BaseSecondsPerMeter => 0;  // Not used for driving - we use speed instead

        public double Speed { get; set; }  // meters per minute

        public DrivingActivity(
            string activityType,
            string entityId,
            double entityLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            double speed)
            : base(activityType, entityId, entityLength, location, area, controlUnit, requestedAt)
        {
            Speed = speed;
        }

        // For driving activities, calculate duration based on distance and speed
        public TimeSpan CalculateDrivingDuration(double distanceMeters)
        {
            if (Speed <= 0)
                return TimeSpan.FromMinutes(1); // Default minimum

            double minutes = distanceMeters / Speed;
            return TimeSpan.FromMinutes(minutes);
        }
    }
}