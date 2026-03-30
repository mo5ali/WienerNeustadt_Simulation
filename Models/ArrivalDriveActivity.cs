using System;
using WienerNeustadtSimulation.Engine;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Represents the activity of a train driving itself to its assigned arrival track
    /// on its own locomotive (no resource requests, fixed time).
    /// </summary>
    public class ArrivalDriveActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

        // You can add arrival-specific flags if needed

        public ArrivalDriveActivity(
            SimulationEngine engine,
            string trainId,
            string arrivalTrackId,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base(
                activityType: "ArrivalDrive",
                entityId: trainId,
                entityLength: 0,
                location: arrivalTrackId,
                area: area,
                controlUnit: controlUnit,
                requestedAt: requestedAt)
        {
        }

        /// <summary>
        /// Returns the fixed (or computed) duration of the arrival drive.
        /// Optionally: Compute based on track assignment in the future.
        /// </summary>
        public TimeSpan CalculateFixedDuration()
        {
            double distanceMeters = 500.0;         // TODO: parameterize by track in future
            double speedMetersPerMinute = 25.0;    // Assume train speed, or get from config
            return TimeSpan.FromMinutes(distanceMeters / speedMetersPerMinute);
        }
    }
}