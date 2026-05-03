using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Infrastructure;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Control;
using WienerNeustadtSimulation.Output;

namespace WienerNeustadtSimulation.Models
{
    public class PushOffActivity : Activity
    {
        public override int RequiredWorkers => 3;
        public override bool RequiresLocomotive => false; // Loco is carried over from ITP via AllocatedLocoIds
        public override double BaseSecondsPerMeter => 8.0;

        // The loco finishes its job when the push-off is complete and must go back to the pool.
        public override bool LocoStaysWithEntity => false;

        // Workers are released individually: each one starts walking back and is immediately
        // available for dispatch to the next waiting request.
        public override bool WorkersReleasedIndividually => true;

        public List<string> WagonGroupIds { get; set; }

        private List<WagonGroupPush> _pushGroups = new List<WagonGroupPush>();
        private int _completedPushes = 0;
        private readonly SimulationEngine _engine;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;
        private readonly ClassificationControlUnit _classificationControl;

        // Late-binding resolver supplied by ArrivalCU. Called per sub-push so a
        // destination committed to an OBT after sort time gets routed to a
        // fresh classification track instead of stacking onto the now-committed
        // one. Returns null if no free track is available — the affected
        // sub-push is then logged and skipped.
        private readonly Func<string, Track?> _resolveTrackForDestination;

        public PushOffActivity(
            string trainId,
            double trainLength,
            List<string> wagonGroupIds,
            string fromLocation,
            string area,
            DateTime requestedAt,
            SimulationEngine engine,
            Dictionary<string, WagonGroupDto> wagonGroupData,
            ClassificationControlUnit classificationControl,
            Func<string, Track?> resolveTrackForDestination)
            : base("PushOff", trainId, trainLength, fromLocation, area, "ArrivalCU", requestedAt)
        {
            WagonGroupIds = wagonGroupIds ?? new List<string>();
            _engine = engine;
            _wagonGroupData = wagonGroupData;
            _classificationControl = classificationControl;
            _resolveTrackForDestination = resolveTrackForDestination
                ?? throw new ArgumentNullException(nameof(resolveTrackForDestination));
        }

        public void CommencePushOff()
        {
            MarkCommenced(_engine.Now);

            // "initialized" is already printed by the base Activity constructor.
            // Only print "Commence" here.
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: Commence {ActivityId}");

            // Group BY DESTINATION NAME, not by track. Tracks are resolved
            // late, per sub-push, in ExecuteNextPush — see _resolveTrackForDestination.
            _pushGroups = GroupConsecutiveWagonsByDestination();

            SimulationLogger.Instance.LogTrainEvent(EntityId, "PushOffStarted", _engine.Now, $"{_pushGroups.Count} groups");

            ExecuteNextPush();
        }

        private void ExecuteNextPush()
        {
            if (_completedPushes >= _pushGroups.Count)
            {
                CompletePushOff();
                return;
            }

            var group = _pushGroups[_completedPushes];
            string combinedId = string.Join("+", group.WagonGroupIds);

            // Late-bind the destination track at this exact moment. If the
            // destination was committed to an OBT since sort time (or since
            // the previous sub-push of this same activity), the resolver
            // returns a fresh classification track instead of the committed
            // one. This is the key invariant the user wants: don't push new
            // WGs onto a track that is already forming an outgoing train.
            var destinationTrack = _resolveTrackForDestination(group.DestinationName);
            if (destinationTrack == null)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: ERROR — no track available for destination '{group.DestinationName}', skipping group [{combinedId}]");
                SimulationLogger.Instance.LogWagonGroupEvent(combinedId, "PushSkipped_NoTrack", _engine.Now, group.DestinationName);
                _completedPushes++;
                ExecuteNextPush();
                return;
            }
            group.DestinationTrack = destinationTrack;

            var driveActivity = new DrivingActivity(
                activityType: "PushOffDrive",
                entityId: combinedId,
                entityLength: group.TotalLength,
                location: destinationTrack.RealLifeID,
                area: destinationTrack.Area,
                controlUnit: "PushOff",
                requestedAt: _engine.Now,
                speed: 20
            );

            driveActivity.AllocatedLocoIds.AddRange(this.AllocatedLocoIds);

            if (driveActivity.AllocatedLocoIds.Count > 0)
                driveActivity.RecordLocoArrival(driveActivity.AllocatedLocoIds[0], _engine.Now);

            double distanceMeters = 100.0;
            var pushDuration = driveActivity.CalculateDrivingDuration(distanceMeters);

            driveActivity.MarkCommenced(_engine.Now);
            driveActivity.ScheduledCompletionAt = _engine.Now.Add(pushDuration);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff drive: {distanceMeters:F0}m {pushDuration.TotalSeconds:F0}s, {EntityId} ({group.DestinationName}) to track {destinationTrack.RealLifeID}");
            SimulationLogger.Instance.LogWagonGroupEvent(combinedId, "PushingToTrack", _engine.Now, destinationTrack.RealLifeID);

            _engine.Schedule(
                _engine.Now.Add(pushDuration),
                () => OnPushCompleted(group, driveActivity),
                $"PushComplete-{ActivityId}-{_completedPushes}"
            );
        }

        private void OnPushCompleted(WagonGroupPush group, DrivingActivity driveActivity)
        {
            driveActivity.MarkCompleted(_engine.Now);

            foreach (var wgId in group.WagonGroupIds)
            {
                if (!_wagonGroupData.ContainsKey(wgId))
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: WARNING - wagon group {wgId} not found in data");
                    continue;
                }

                var wgData = _wagonGroupData[wgId];

                var wagonGroup = new WagonGroup(
                    id: wgId,
                    length: wgData.Length ?? 0,
                    destination: wgData.Destination ?? "Unknown",
                    wagonIds: new List<string>()
                );

                wagonGroup.CurrentTrackId = group.DestinationTrack.RealLifeID;
                wagonGroup.CurrentArea = group.DestinationTrack.Area;
                wagonGroup.ArrivedAt = _engine.Now;

                group.DestinationTrack.CurrentOccupancies.Add(wgId);

                SimulationLogger.Instance.LogWagonGroupEvent(wgId, "EntityCreated", _engine.Now, group.DestinationTrack.RealLifeID);

                _classificationControl.HandleWagonGroupArrival(wagonGroup, group.DestinationTrack, _engine.Now);
            }

            _completedPushes++;
            ExecuteNextPush();
        }

        private void CompletePushOff()
        {
            MarkCompleted(_engine.Now);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: DONE '{ActivityId}' - train {EntityId} dismantled");
            SimulationLogger.Instance.LogTrainEvent(EntityId, "PushOffComplete", _engine.Now);

            OnCompleted?.Invoke(this);
        }

        // Group consecutive WGs by destination NAME (not by track). Tracks are
        // bound later, in ExecuteNextPush, via _resolveTrackForDestination — so
        // a destination that gets committed to an OBT mid-PushOff produces a
        // brand-new track for any subsequent group of the same destination.
        //
        // WGs without a known destination (no entry in _wagonGroupData or empty
        // Destination) are skipped here so they don't create a phantom group.
        private List<WagonGroupPush> GroupConsecutiveWagonsByDestination()
        {
            var groups = new List<WagonGroupPush>();
            if (WagonGroupIds.Count == 0) return groups;

            WagonGroupPush? currentGroup = null;

            foreach (var wgId in WagonGroupIds)
            {
                var destinationName = GetWagonGroupDestination(wgId);
                if (string.IsNullOrEmpty(destinationName))
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: WARNING - wagon group {wgId} has no destination, skipping");
                    continue;
                }

                if (currentGroup != null && currentGroup.DestinationName == destinationName)
                {
                    currentGroup.WagonGroupIds.Add(wgId);
                    currentGroup.TotalLength += GetWagonGroupLength(wgId);
                }
                else
                {
                    if (currentGroup != null) groups.Add(currentGroup);
                    currentGroup = new WagonGroupPush
                    {
                        DestinationName = destinationName,
                        WagonGroupIds = new List<string> { wgId },
                        TotalLength = GetWagonGroupLength(wgId)
                    };
                }
            }

            if (currentGroup != null) groups.Add(currentGroup);
            return groups;
        }

        private double GetWagonGroupLength(string wagonGroupId)
        {
            if (_wagonGroupData.ContainsKey(wagonGroupId))
                return _wagonGroupData[wagonGroupId].Length ?? 0;
            return 0;
        }

        private string GetWagonGroupDestination(string wagonGroupId)
        {
            if (_wagonGroupData.ContainsKey(wagonGroupId))
                return _wagonGroupData[wagonGroupId].Destination ?? string.Empty;
            return string.Empty;
        }

        private class WagonGroupPush
        {
            // Set at grouping time. Stable for the lifetime of the group.
            public string DestinationName { get; set; } = string.Empty;
            // Set late, in ExecuteNextPush, via _resolveTrackForDestination.
            // May resolve to a different track for each group of the same
            // destination if an OBT was committed in between.
            public Track DestinationTrack { get; set; } = null!;
            public List<string> WagonGroupIds { get; set; } = new List<string>();
            public double TotalLength { get; set; }
        }
    }
}
