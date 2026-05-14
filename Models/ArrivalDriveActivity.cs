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
        // Tracks closer to the entry queue get shorter drives; the distances
        // here are illustrative — adjust to match the actual yard geometry
        // when better data is available. Unknown tracks fall back to
        // FALLBACK_DISTANCE_M so a typo'd track id never crashes the sim.
        private static readonly Dictionary<string, double> TrackDistanceMeters =
            new Dictionary<string, double>
            {
                { "731", 250 }, { "729", 290 }, { "727", 330 }, { "725", 380 },
                { "723", 430 }, { "721", 480 }, { "719", 530 }, { "717", 580 },
                { "715", 630 }, { "713", 680 }, { "711", 720 }, { "709", 770 },
                { "707", 820 }, { "705", 870 }, { "703", 920 },
            };
        private const double FALLBACK_DISTANCE_M = 500;

        // Train speed during ArrivalDrive (constant; unchanged from before).
        private const double TRAIN_SPEED_M_PER_MIN = 100;

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