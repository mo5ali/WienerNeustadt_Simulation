using System;
using WienerNeustadtSimulation.Engine;

namespace WienerNeustadtSimulation.Models
{
    public class DepartureDriveActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

        public bool RequiresExitGate => true;  // Custom flag for exit gate

        public DepartureDriveActivity(
            SimulationEngine engine,  // FIX: Correct namespace
            string trainId,
            string trackId,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base(
                activityType: "DepartureDrive",
                entityId: trainId,
                entityLength: 0,
                location: trackId,
                area: area,
                controlUnit: controlUnit,
                requestedAt: requestedAt)
        {
        }

        public TimeSpan CalculateFixedDuration()
        {
            double distanceMeters = 500.0;
            double speedMetersPerMinute = 25.0;
            return TimeSpan.FromMinutes(distanceMeters / speedMetersPerMinute);
        }
    }
}