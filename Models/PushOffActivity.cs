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
        public Dictionary<string, Track> WagonGroupDestinations { get; set; }

        private List<WagonGroupPush> _pushGroups = new List<WagonGroupPush>();
        private int _completedPushes = 0;
        private readonly SimulationEngine _engine;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;
        private readonly ClassificationControlUnit _classificationControl;

        public PushOffActivity(
            string trainId,
            double trainLength,
            List<string> wagonGroupIds,
            Dictionary<string, Track> wagonGroupDestinations,
            string fromLocation,
            string area,
            DateTime requestedAt,
            SimulationEngine engine,
            Dictionary<string, WagonGroupDto> wagonGroupData,
            ClassificationControlUnit classificationControl)
            : base("PushOff", trainId, trainLength, fromLocation, area, "ArrivalCU", requestedAt)
        {
            WagonGroupIds = wagonGroupIds ?? new List<string>();
            WagonGroupDestinations = wagonGroupDestinations ?? new Dictionary<string, Track>();
            _engine = engine;
            _wagonGroupData = wagonGroupData;
            _classificationControl = classificationControl;
        }

        public void CommencePushOff()
        {
            CommencedAt = _engine.Now;

            // "initialized" is already printed by the base Activity constructor.
            // Only print "Commence" here.
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: Commence {ActivityId}");
            SimulationLogger.Instance.LogTrainEvent(EntityId, "PushOffStarted", _engine.Now, $"{_pushGroups.Count} groups");

            _pushGroups = GroupConsecutiveWagonsByDestination();

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

            var driveActivity = new DrivingActivity(
                activityType: "PushOffDrive",
                entityId: combinedId,
                entityLength: group.TotalLength,
                location: group.DestinationTrack.RealLifeID,
                area: group.DestinationTrack.Area,
                controlUnit: "PushOff",
                requestedAt: _engine.Now,
                speed: 25.0
            );

            driveActivity.AllocatedLocoIds.AddRange(this.AllocatedLocoIds);

            if (driveActivity.AllocatedLocoIds.Count > 0)
                driveActivity.RecordLocoArrival(driveActivity.AllocatedLocoIds[0], _engine.Now);

            double distanceMeters = 100.0;
            var pushDuration = driveActivity.CalculateDrivingDuration(distanceMeters);

            driveActivity.CommencedAt = _engine.Now;
            driveActivity.ScheduledCompletionAt = _engine.Now.Add(pushDuration);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: {driveActivity.AllocatedLocoIds[0]} driving {distanceMeters:F0}m to track {group.DestinationTrack.RealLifeID} (ETA {pushDuration.TotalSeconds:F0}s)");
            SimulationLogger.Instance.LogWagonGroupEvent(string.Join("+", group.WagonGroupIds), "PushingToTrack", _engine.Now, group.DestinationTrack.RealLifeID);

            _engine.Schedule(
                _engine.Now.Add(pushDuration),
                () => OnPushCompleted(group, driveActivity),
                $"PushComplete-{ActivityId}-{_completedPushes}"
            );
        }

        private void OnPushCompleted(WagonGroupPush group, DrivingActivity driveActivity)
        {
            driveActivity.CompletedAt = _engine.Now;

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
            CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | PushOff: DONE '{ActivityId}' - train {EntityId} dismantled");
            SimulationLogger.Instance.LogTrainEvent(EntityId, "PushOffComplete", _engine.Now);

            OnCompleted?.Invoke(this);
        }

        private List<WagonGroupPush> GroupConsecutiveWagonsByDestination()
        {
            var groups = new List<WagonGroupPush>();

            if (WagonGroupIds.Count == 0)
                return groups;

            var currentGroup = new WagonGroupPush
            {
                DestinationTrack = WagonGroupDestinations[WagonGroupIds[0]],
                WagonGroupIds = new List<string> { WagonGroupIds[0] },
                TotalLength = GetWagonGroupLength(WagonGroupIds[0])
            };

            for (int i = 1; i < WagonGroupIds.Count; i++)
            {
                var wgId = WagonGroupIds[i];
                var destination = WagonGroupDestinations[wgId];

                if (destination.RealLifeID == currentGroup.DestinationTrack.RealLifeID)
                {
                    currentGroup.WagonGroupIds.Add(wgId);
                    currentGroup.TotalLength += GetWagonGroupLength(wgId);
                }
                else
                {
                    groups.Add(currentGroup);
                    currentGroup = new WagonGroupPush
                    {
                        DestinationTrack = destination,
                        WagonGroupIds = new List<string> { wgId },
                        TotalLength = GetWagonGroupLength(wgId)
                    };
                }
            }

            groups.Add(currentGroup);
            return groups;
        }

        private double GetWagonGroupLength(string wagonGroupId)
        {
            if (_wagonGroupData.ContainsKey(wagonGroupId))
                return _wagonGroupData[wagonGroupId].Length ?? 0;
            return 0;
        }

        private class WagonGroupPush
        {
            public Track DestinationTrack { get; set; } = null!;
            public List<string> WagonGroupIds { get; set; } = new List<string>();
            public double TotalLength { get; set; }
        }
    }
}