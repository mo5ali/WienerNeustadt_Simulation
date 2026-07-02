using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Models
{
    public class ManipulationActivity : Activity
    {
        // ITP duration formula (supervisor spec, updated):
        //
        //   duration_seconds
        //     = max(joints, 1) × BASE_SECONDS_PER_JOINT × averageWorkerMultiplier   (joint term)
        //       + (trainLength / walkingSpeed) × 60                                  (walk term)
        //
        // Joint term: the hands-on work of separating the train at each
        // separation joint — uncoupling and fitting/removing the removable
        // link used to split the wagon-group cuts during push-off. Scales with
        // the number of separation joints (consecutive same-destination cuts − 1)
        // and the average ITP-skill multiplier of the allocated workers
        // (default 1.0 if no skill entry).
        // Walk term: the time for the workers to walk the length of the train
        // for the visual checks / info confirmation done during ITP.
        // trainLength (m) ÷ walkingSpeed (m/min) gives minutes; × 60 → seconds.
        // walkingSpeed is the workers' station MovementSpeedMetersPerMinute
        // (the same speed used for their travel to the work site), averaged
        // across the allocated ITP workers and set by ArrivalControlUnit.
        //
        // No joint floor: a single-destination train (0 joints) keeps its road
        // locomotive until push-off, so it needs no separation work — its ITP
        // duration is purely the walk term. Tune BASE_SECONDS_PER_JOINT to
        // recalibrate the joint term.
        private const double BASE_SECONDS_PER_JOINT = 90.0;   // 1.5 min per separation joint (was 120)
        // Fallback worker walking speed (m/min) for the walk term, used only if
        // no allocated-worker speed is available. Same magnitude as
        // ResourceControlUnit.DefaultWorkerSpeedMetersPerMinute.
        private const double FALLBACK_WALK_SPEED_M_PER_MIN = 80.0;
        public override int RequiredWorkers => GetRequiredWorkersForType(ActivityType);
        public override bool RequiresLocomotive => GetRequiresLocomotiveForType(ActivityType);
        public override double BaseSecondsPerMeter => GetBaseSecondsPerMeterForType(ActivityType);

        // IncomingTrainPreparation allocates the loco that then stays for the PushOff.
        // All other manipulation activities have no loco.
        public override bool LocoStaysWithEntity => ActivityType == "IncomingTrainPreparation";

        // Workers are always returned as a batch for manipulation activities.
        public override bool WorkersReleasedIndividually => false;

        // Number of separation joints the upcoming sort/push will require.
        // Only meaningful for IncomingTrainPreparation; computed in
        // ArrivalControlUnit right after RunSortingMethod (= consecutive
        // same-destination cuts − 1) and passed in at construction time.
        // The ITP duration formula in CalculateDuration (task #24) uses it
        // as the main driver of work time.
        public int SeparationJoints { get; }

        // Average walking speed (m/min) of the workers allocated to this ITP,
        // set by ArrivalControlUnit just before CalculateDuration is called.
        // Drives the walk term of the ITP formula. Defaults to the fallback
        // until set.
        public double WalkingSpeedMetersPerMinute { get; set; } = FALLBACK_WALK_SPEED_M_PER_MIN;

        public ManipulationActivity(
            string activityType,
            string entityId,
            double entityLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            int separationJoints = 0)
            : base(activityType, entityId, entityLength, location, area, controlUnit, requestedAt,
                   // Surface the joint count in the Submitted-event details so
                   // analytics can verify it without re-deriving from sort output.
                   extraDetails: activityType == "IncomingTrainPreparation"
                                 ? $"joints={separationJoints}"
                                 : null)
        {
            SeparationJoints = separationJoints;
        }

        // Override for IncomingTrainPreparation: drive duration off the
        // separation-joint count instead of the generic
        // length × BaseSecondsPerMeter × worker_multiplier formula every
        // other manipulation type uses. Everything else (Securing, Coupling,
        // Uncoupling, PushOff) keeps the base-class formula via the
        // fall-through to base.CalculateDuration.
        public override TimeSpan CalculateDuration(Dictionary<string, double> workerMultipliers)
        {
            if (ActivityType != "IncomingTrainPreparation")
                return base.CalculateDuration(workerMultipliers);

            var modifiers = new List<double>();
            foreach (var workerId in AllocatedWorkerIds)
            {
                if (workerMultipliers.ContainsKey(workerId))
                    modifiers.Add(workerMultipliers[workerId]);
            }
            AverageWorkerMultiplier = modifiers.Count > 0 ? modifiers.Average() : 1.0;

            // No floor: a single-destination (0-joint) train keeps its road
            // locomotive until push-off and needs no joint separation work, so
            // its joint term is 0 and the ITP duration is purely the walk term.
            int joints = SeparationJoints;
            // Joint term: separation work, scaled by the worker ITP-skill multiplier.
            double jointSeconds = joints
                                  * BASE_SECONDS_PER_JOINT
                                  * AverageWorkerMultiplier.Value;
            // Walk term: workers walking the train length for visual checks.
            // length (m) / speed (m/min) = minutes; × 60 → seconds.
            double walkSpeed = WalkingSpeedMetersPerMinute > 0
                ? WalkingSpeedMetersPerMinute
                : FALLBACK_WALK_SPEED_M_PER_MIN;
            double walkSeconds = (EntityLength / walkSpeed) * 60.0;
            double seconds = jointSeconds + walkSeconds;
            CalculatedDuration = TimeSpan.FromSeconds(seconds);
            return CalculatedDuration.Value;
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