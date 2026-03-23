using System;

namespace WienerNeustadtSimulation.Models
{
    public class DrivingActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => true;
        public override double BaseSecondsPerMeter => 0;

        // The loco departs with the train entity and must NOT be returned to the pool
        // by Release(). The ClassificationCU calls ReturnLoco() explicitly only after
        // the outbound train physically exits the system in CompleteDepartureDrive().
        public override bool LocoStaysWithEntity => true;

        // No workers involved.
        public override bool WorkersReleasedIndividually => false;

        public double Speed { get; set; }

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

        public TimeSpan CalculateDrivingDuration(double distanceMeters)
        {
            if (Speed <= 0)
                return TimeSpan.FromMinutes(1);

            double minutes = distanceMeters / Speed;
            return TimeSpan.FromMinutes(minutes);
        }
    }
}