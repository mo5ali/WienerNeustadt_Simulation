using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Models
{
    public class ManipulationActivity : Activity
    {
        // ITP duration formula constants (supervisor spec, June 2026).
        //
        //   duration_seconds
        //     = max(joints, 1) × BASE_SECONDS_PER_JOINT
        //                      × (trainLength / REF_LENGTH_M)
        //                      × averageWorkerMultiplier
        //
        // The "joints" main factor is the count of consecutive
        // same-destination cuts minus 1 (a 3-cut train has 2 joints —
        // 2 places where workers physically separate the train during
        // preparation). The max() floor recognises that even a
        // single-destination train (0 joints) still needs the road-loco
        // decouple + brake-line work, which is on the same order as
        // one joint's worth of effort. The length factor is a linear
        // scale: a 200 m train multiplies the per-joint base time by
        // 2.0; a 50 m train by 0.5. The worker multiplier is the
        // average of allocated workers' ITP-skill multipliers
        // (default 1.0 if no skill entry).
        //
        // Tune BASE_SECONDS_PER_JOINT and REF_LENGTH_M independently
        // to recalibrate without touching the formula structure.
        private const double BASE_SECONDS_PER_JOINT = 120.0;  // 2 min per separation joint
        private const double REF_LENGTH_M = 100.0;            // 1.0× factor at 100 m
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

            int effectiveJoints = Math.Max(SeparationJoints, 1);
            double lengthFactor = EntityLength / REF_LENGTH_M;
            double seconds = effectiveJoints
                             * BASE_SECONDS_PER_JOINT
                             * lengthFactor
                             * AverageWorkerMultiplier.Value;
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