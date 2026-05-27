using System;
using System.Collections.Generic;
using WienerNeustadtSimulation.Engine;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Represents the activity of a train driving itself to its assigned arrival track
    /// on its own locomotive (no resource requests).
    ///
    /// Duration is no longer flat — it's dominated by the per-track distance from
    /// the entry gate (BIG weight, set per arrival-track id) plus a smaller
    /// contribution from the train's length. See [18] in Other files/Documentation.txt
    /// for the rationale and the screenshot of the flat-duration bug it fixes.
    /// </summary>
    public class ArrivalDriveActivity : Activity
    {
        public override int RequiredWorkers => 0;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

        private readonly string _arrivalTrackId;
        private readonly double _trainLength;

        // Distance from the entry gate to each arrival track, in meters.
        // Source: real-world Mercator-projection measurements from the
        // station data (provided by user, 2025). Note the non-monotonic
        // pattern (e.g. 717 < 721, 729 == 731) which reflects actual yard
        // geometry — switches and crossovers don't lay out as a clean
        // ladder. Unknown tracks fall back to FALLBACK_DISTANCE_M so a
        // typo'd track id never crashes the sim.
        private static readonly Dictionary<string, double> TrackDistanceMeters =
            new Dictionary<string, double>
            {
                { "703", 766.25 }, { "705", 765.77 }, { "707", 670.93 },
                { "709", 629.33 }, { "711", 587.72 }, { "713", 548.41 },
                { "715", 521.53 }, { "717", 471.68 }, { "719", 505.47 },
                { "721", 395.90 }, { "723", 435.33 }, { "725", 476.05 },
                { "727", 556.20 }, { "729", 587.14 }, { "731", 587.14 },
            };
        private const double FALLBACK_DISTANCE_M = 550;

        // Train (own road locomotive) speed entering the yard. Yard movements
        // are governed by "restricted speed" (<= ~32 km/h) and Class-1 yard
        // track (~16-24 km/h); since an arriving train runs at restricted
        // speed through the throat and decelerates to a dead stop on its
        // arrival track, we use an effective 20 km/h = 333.33 m/min. Same
        // value as DepartureDrive (leaving) for consistency. (Was 100 m/min
        // = ~6 km/h, unrealistically slow — see [20].)
        private const double TRAIN_SPEED_M_PER_MIN = 333.33; // 20 km/h

        // Length penalty: extra seconds added per meter of train length.
        // Kept small so per-track distance stays the dominant factor — a
        // 200m train adds only 40s, while track distance contributes
        // 150-552s across the yard.
        private const double LENGTH_PENALTY_S_PER_M = 0.2;

        public ArrivalDriveActivity(
            SimulationEngine engine,
            string trainId,
            string arrivalTrackId,
            string area,
            string controlUnit,
            DateTime requestedAt,
            double trainLength = 0)
            : base(
                activityType: "ArrivalDrive",
                entityId: trainId,
                entityLength: trainLength,
                location: arrivalTrackId,
                area: area,
                controlUnit: controlUnit,
                requestedAt: requestedAt)
        {
            _arrivalTrackId = arrivalTrackId;
            _trainLength = trainLength;
        }

        /// <summary>
        /// duration_seconds
        ///   = (track_distance_m / TRAIN_SPEED_M_PER_MIN) * 60
        ///     + train_length_m * LENGTH_PENALTY_S_PER_M
        ///
        /// First term is the dominant per-track contribution. Second term
        /// adds a small length-dependent penalty so longer trains take a
        /// few extra seconds to fully clear the entry switch.
        /// </summary>
        public TimeSpan CalculateFixedDuration()
        {
            double distance = TrackDistanceMeters.TryGetValue(_arrivalTrackId, out var d)
                ? d
                : FALLBACK_DISTANCE_M;
            double distanceSeconds = (distance / TRAIN_SPEED_M_PER_MIN) * 60.0;
            double lengthSeconds = _trainLength * LENGTH_PENALTY_S_PER_M;
            return TimeSpan.FromSeconds(distanceSeconds + lengthSeconds);
        }
    }
}