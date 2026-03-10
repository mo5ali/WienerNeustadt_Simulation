using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Infrastructure;

namespace WienerNeustadtSimulation.Models
{
    public class PushOffActivity : Activity
    {
        public override int RequiredWorkers => 3;
        public override bool RequiresLocomotive => false;  // Loco inherited from ITP
        public override double BaseSecondsPerMeter => 8.0;

        public List<string> WagonGroupIds { get; set; }
        public Dictionary<string, Track> WagonGroupDestinations { get; set; }

        private List<WagonGroupPush> _pushGroups = new List<WagonGroupPush>();  // Initialize to avoid warning
        private int _completedPushes = 0;
        private SimulationEngine _engine;
        private Dictionary<string, WagonGroupDto> _wagonGroupData;

        public PushOffActivity(
            string trainId,
            double trainLength,
            List<string> wagonGroupIds,
            Dictionary<string, Track> wagonGroupDestinations,
            string fromLocation,
            string area,
            DateTime requestedAt,
            SimulationEngine engine,
            Dictionary<string, WagonGroupDto> wagonGroupData)
            : base("PushOff", trainId, trainLength, fromLocation, area, "ArrivalCU", requestedAt)
        {
            WagonGroupIds = wagonGroupIds ?? new List<string>();
            WagonGroupDestinations = wagonGroupDestinations ?? new Dictionary<string, Track>();
            _engine = engine;
            _wagonGroupData = wagonGroupData;
        }

        public void CommencePushOff()
        {
            CommencedAt = _engine.Now;

            _pushGroups = GroupConsecutiveWagonsByDestination();

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: dismantling train {EntityId} into {_pushGroups.Count} push group(s)");

            foreach (var group in _pushGroups)
            {
                string wgList = string.Join(", ", group.WagonGroupIds);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: group [{wgList}] → track {group.DestinationTrack.RealLifeID} ({group.TotalLength:F1}m)");
            }

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

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: pushing wagon group(s) [{string.Join(", ", group.WagonGroupIds)}] to track {group.DestinationTrack.RealLifeID}");

            var driveActivity = new DrivingActivity(
                activityType: "PushOffDrive",
                entityId: combinedId,
                entityLength: group.TotalLength,
                location: group.DestinationTrack.RealLifeID,
                area: group.DestinationTrack.Area,
                controlUnit: "PushOff",
                requestedAt: _engine.Now,
                speed: 25.0  // ← No comma, no autoSubmit
            );

            driveActivity.AllocatedLocoIds.AddRange(this.AllocatedLocoIds);

            if (driveActivity.AllocatedLocoIds.Count > 0)
            {
                driveActivity.RecordLocoArrival(driveActivity.AllocatedLocoIds[0], _engine.Now);
            }

            double distanceMeters = 100.0;
            var pushDuration = driveActivity.CalculateDrivingDuration(distanceMeters);

            driveActivity.CommencedAt = _engine.Now;
            driveActivity.ScheduledCompletionAt = _engine.Now.Add(pushDuration);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: {driveActivity.AllocatedLocoIds[0]} driving {distanceMeters:F0}m to track {group.DestinationTrack.RealLifeID} (ETA {pushDuration.TotalSeconds:F0}s)");

            _engine.Schedule(
                _engine.Now.Add(pushDuration),
                () => OnPushCompleted(group, driveActivity),
                $"PushComplete-{ActivityId}-{_completedPushes}"
            );
        }

        private void OnPushCompleted(WagonGroupPush group, DrivingActivity driveActivity)
        {
            driveActivity.CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: wagon group(s) [{string.Join(", ", group.WagonGroupIds)}] arrived at track {group.DestinationTrack.RealLifeID}");

            foreach (var wgId in group.WagonGroupIds)
            {
                group.DestinationTrack.CurrentOccupancies.Add(wgId);
            }

            _completedPushes++;
            ExecuteNextPush();
        }

        private void CompletePushOff()
        {
            CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | PushOff: DONE '{ActivityId}' - train {EntityId} dismantled, all wagon groups pushed");

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
            public Track DestinationTrack { get; set; } = null!;  // Will be set immediately
            public List<string> WagonGroupIds { get; set; } = new List<string>();
            public double TotalLength { get; set; }
        }
    }
}