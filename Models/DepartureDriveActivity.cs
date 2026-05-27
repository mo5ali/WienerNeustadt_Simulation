using System;
using System.Collections.Generic;
using WienerNeustadtSimulation.Engine;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Represents the outbound train driving itself from its classification
    /// track, out through the exit gate, and off the yard on its own
    /// locomotive (no worker/shunt-loco requests; the exit gate is acquired
    /// separately via RequiresExitGate).
    ///
    /// Duration is no longer flat — it mirrors the ArrivalDrive treatment
    /// ([18]): a per-track distance from the classification track to the exit
    /// gate (BIG weight) plus a small length-dependent penalty. See [20] in
    /// Other files/Documentation.txt for the rationale, node paths and sources.
    /// </summary>
    public class DepartureDriveActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

        public bool RequiresExitGate => true;  // Custom flag for exit gate

        private readonly string _trackId;
        private readonly double _trainLength;

        // Distance from each classification track to the exit gate
        // (infrastructure node 775), in meters. Exact Euclidean polyline
        // lengths summed from the UTM node coordinates in
        // Infrastructure_WienerNeustadt_V20.json. Identical pairs (605/607,
        // 617/619, 621/623, 627/629) share the same node path, which reflects
        // the real shared switch geometry rather than a clean ladder.
        // Unknown tracks fall back to FALLBACK_DISTANCE_M (~ median).
        private static readonly Dictionary<string, double> TrackDistanceMeters =
            new Dictionary<string, double>
            {
                { "605", 286.14 }, { "607", 286.14 }, { "609", 259.16 },
                { "611", 231.94 }, { "613", 205.90 }, { "615", 223.61 },
                { "617", 262.93 }, { "619", 262.93 }, { "621", 259.31 },
                { "623", 259.31 }, { "625", 240.31 }, { "627", 188.33 },
                { "629", 182.11 },
            };
        private const double FALLBACK_DISTANCE_M = 245;

        // Train (own road locomotive) speed leaving the yard. Yard movements
        // are governed by "restricted speed" (<= ~32 km/h) and Class-1 yard
        // track (~16-24 km/h); since a departing train accelerates from a
        // dead stop on the classification track and runs at restricted speed
        // through the throat, we use an effective 20 km/h = 333.33 m/min.
        // Same value used for ArrivalDrive (entering) for consistency.
        private const double TRAIN_SPEED_M_PER_MIN = 333.33; // 20 km/h

        // Length penalty: extra seconds per meter of train length, kept small
        // so per-track distance stays the dominant factor (matches [18]).
        private const double LENGTH_PENALTY_S_PER_M = 0.2;

        public DepartureDriveActivity(
            SimulationEngine engine,
            string trainId,
            string trackId,
            string area,
            string controlUnit,
            DateTime requestedAt,
            double trainLength = 0)
            : base(
                activityType: "DepartureDrive",
                entityId: trainId,
                entityLength: trainLength,
                location: trackId,
                area: area,
                controlUnit: controlUnit,
                requestedAt: requestedAt)
        {
            _trackId = trackId;
            _trainLength = trainLength;
        }

        /// <summary>
        /// duration_seconds
        ///   = (track_distance_m / TRAIN_SPEED_M_PER_MIN) * 60
        ///     + train_length_m * LENGTH_PENALTY_S_PER_M
        /// </summary>
        public TimeSpan CalculateFixedDuration()
        {
            double distance = TrackDistanceMeters.TryGetValue(_trackId, out var d)
                ? d
                : FALLBACK_DISTANCE_M;
            double distanceSeconds = (distance / TRAIN_SPEED_M_PER_MIN) * 60.0;
            double lengthSeconds = _trainLength * LENGTH_PENALTY_S_PER_M;
            return TimeSpan.FromSeconds(distanceSeconds + lengthSeconds);
        }
    }
}
