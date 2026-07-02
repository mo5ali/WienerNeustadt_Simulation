using System;
using System.Collections.Generic;
using System.Linq;

namespace WienerNeustadtSimulation.Models
{
    public class CouplingActivity : Activity
    {
        // Fixed hands-on coupling time — coupling the incoming cut to the
        // standing rake at the single interface: connect the hook-and-screw
        // link and the air/brake hoses between the two vehicles. This is ONE
        // physical coupling regardless of how many wagon groups are in the cut,
        // so the duration is fixed (NOT length-scaled) and only scaled by the
        // worker's Coupling-skill multiplier. ~4 min matches the reported
        // 3-5 min for a manual European screw coupling (DAC trials benchmark
        // manual at 4-6 min). See [34] in Other files/Documentation.txt.
        private const double FIXED_SECONDS = 240.0;

        public override int RequiredWorkers => 1;
        public override bool RequiresLocomotive => false;
        public override double BaseSecondsPerMeter => 0;  // duration is fixed, not length-based

        // Set at construction time: true when coupling a locomotive to an outbound train
        // (loco stays with the train until departure); false for wagon group coupling.
        public override bool LocoStaysWithEntity { get; }

        // Workers are released individually so each is immediately dispatchable en-route
        // back to the waiting area.
        public override bool WorkersReleasedIndividually => true;

        public string WagonGroupId { get; set; }
        public string CouplingToTrainId { get; set; }

        public CouplingActivity(
            string wagonGroupId,
            double wagonGroupLength,
            string couplingToTrainId,
            string location,
            string area,
            string controlUnit,
            DateTime requestedAt,
            bool locoStaysWithEntity = false)
            : base("Coupling", wagonGroupId, wagonGroupLength, location, area, controlUnit, requestedAt)
        {
            WagonGroupId = wagonGroupId;
            CouplingToTrainId = couplingToTrainId;
            LocoStaysWithEntity = locoStaysWithEntity;
        }

        // Fixed duration × average worker Coupling-skill multiplier. The cut
        // length is ignored — coupling to the standing rake is a single
        // interface operation.
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

        protected override string GetActivityAbbreviation(string activityType) => "COP";
    }
}
