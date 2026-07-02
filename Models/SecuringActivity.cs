using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Models
{
    public class SecuringActivity : Activity
    {
        // Fixed hands-on securing time — applying the parking / handbrake to
        // hold the cut on the classification track so it can't roll. It is ONE
        // securing operation regardless of how many wagon groups are in the cut,
        // so the duration is fixed (NOT length-scaled) and only scaled by the
        // worker's Securing-skill multiplier. ~2 min: lighter than a coupling,
        // which additionally connects the inter-vehicle screw coupler + air
        // hoses. See [34] in Other files/Documentation.txt for the rationale
        // and sources.
        private const double FIXED_SECONDS = 120.0;

        public override int RequiredWorkers => 1;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;  // duration is fixed, not length-based

        // No loco involved; single worker goes back to pool as a batch (same thing).
        public override bool LocoStaysWithEntity => false;
        public override bool WorkersReleasedIndividually => false;

        public string WagonGroupId { get; set; }

        public SecuringActivity(
            string wagonGroupId,
            double wagonGroupLength,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt)
            : base("Securing", wagonGroupId, wagonGroupLength, location, area, controlUnit, requestedAt)
        {
            WagonGroupId = wagonGroupId;
        }

        // Fixed duration × average worker Securing-skill multiplier. The cut
        // length is ignored — a securing is a single operation.
        public override TimeSpan CalculateDuration(Dictionary<string, double> workerMultipliers)
        {
            var modifiers = new List<double>();
            foreach (var workerId in AllocatedWorkerIds)
                if (workerMultipliers.ContainsKey(workerId))
                    modifiers.Add(workerMultipliers[workerId]);

            AverageWorkerMultiplier = modifiers.Count > 0 ? modifiers.Average() : 1.0;
            CalculatedDuration = TimeSpan.FromSeconds(FIXED_SECONDS * AverageWorkerMultiplier.Value);
            return CalculatedDuration.Value;
        }

        protected override string GetActivityAbbreviation(string activityType) => "SEC";
    }
}
