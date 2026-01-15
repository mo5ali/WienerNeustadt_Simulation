using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Infrastructure;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Control
{
    /// <summary>
    /// Manages trains on arrival tracks:  preparation, classification, and push-off
    /// </summary>
    public class ArrivalControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ClassificationControlUnit _classificationControl;
        private readonly Dictionary<string, Track> _destinationToTrackMap;
        private readonly List<Track> _classificationTracks;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;
        private readonly Dictionary<string, DateTime> _arrivalTrackEntryTimes;

        public ArrivalControlUnit(
            SimulationEngine engine,
            ClassificationControlUnit classificationControl,
            List<Track> classificationTracks,
            Dictionary<string, WagonGroupDto> wagonGroupData)
        {
            _engine = engine;
            _classificationControl = classificationControl;
            _classificationTracks = classificationTracks;
            _wagonGroupData = wagonGroupData;
            _destinationToTrackMap = new Dictionary<string, Track>();
            _arrivalTrackEntryTimes = new Dictionary<string, DateTime>();
        }

        public void HandleTrainOnArrivalTrack(Train train, Track arrivalTrack)
        {
            _arrivalTrackEntryTimes[train.ID] = _engine.Now;

            var waitTime = TimeSpan.FromMinutes(2);

            _engine.Schedule(
                _engine.Now.Add(waitTime),
                () => StartTrainPreparation(train, arrivalTrack),
                $"StartPreparation-{train.ID}"
            );
        }

        private void StartTrainPreparation(Train train, Track arrivalTrack)
        {
            Console.WriteLine($"  → Starting preparation for train {train.ID}");

            var preparationTime = TimeSpan.FromMinutes(10);

            _engine.Schedule(
                _engine.Now.Add(preparationTime),
                () => DetermineClassificationTracks(train, arrivalTrack),
                $"ClassificationDetermination-{train.ID}"
            );
        }

        private void DetermineClassificationTracks(Train train, Track arrivalTrack)
        {
            Console.WriteLine($"  → Determining classification tracks for train {train.ID}");

            var wgClassificationMap = new Dictionary<string, Track>();

            foreach (var wgId in train.WagonGroupIds)
            {
                if (!_wagonGroupData.ContainsKey(wgId))
                {
                    Console.WriteLine($"  ⚠ Warning: WagonGroup {wgId} not found in input data");
                    continue;
                }

                var wgData = _wagonGroupData[wgId];
                var destination = wgData.Destination;

                if (destination != null && !_destinationToTrackMap.ContainsKey(destination))
                {
                    var assignedTrack = AllocateClassificationTrack(destination);
                    _destinationToTrackMap[destination] = assignedTrack;
                    Console.WriteLine($"  → Destination '{destination}' mapped to track {assignedTrack.StationID}");
                }

                wgClassificationMap[wgId] = _destinationToTrackMap[destination];
                Console.WriteLine($"  → WG {wgId} → Destination '{destination}' → Track {_destinationToTrackMap[destination].StationID}");
            }

            RequestUncoupling(train, arrivalTrack, wgClassificationMap);
        }

        private Track AllocateClassificationTrack(string destination)
        {
            var availableTrack = _classificationTracks
                .Where(t => t.Designation == "Classification")
                .Where(t => !_destinationToTrackMap.ContainsValue(t))
                .FirstOrDefault();

            if (availableTrack == null)
            {
                availableTrack = _classificationTracks
                    .Where(t => t.Designation == "Classification")
                    .OrderBy(t => t.CurrentOccupancies.Count)
                    .First();

                Console.WriteLine($"  ⚠ No free classification tracks - reusing track {availableTrack.StationID}");
            }

            return availableTrack;
        }

        private void RequestUncoupling(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            Console.WriteLine($"  → Requesting uncoupling for train {train.ID}");

            var uncouplingTime = TimeSpan.FromMinutes(train.WagonGroupIds.Count * 3);

            _engine.Schedule(
                _engine.Now.Add(uncouplingTime),
                () => CheckServiceStatus(train, arrivalTrack, wgClassificationMap),
                $"UncouplingComplete-{train.ID}"
            );
        }

        private void CheckServiceStatus(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            bool isServiceOkay = true;

            if (isServiceOkay)
            {
                EndWaitingActivity(train, arrivalTrack, wgClassificationMap);
            }
            else
            {
                Console.WriteLine($"  ⚠ Service not ready for train {train.ID}, retrying.. .");
                _engine.Schedule(
                    _engine.Now.AddMinutes(5),
                    () => CheckServiceStatus(train, arrivalTrack, wgClassificationMap),
                    $"RetryService-{train.ID}"
                );
            }
        }

        private void EndWaitingActivity(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            Console.WriteLine($"  → Preparation of shunting train {train.ID}");

            var preparationTime = TimeSpan.FromMinutes(8);

            _engine.Schedule(
                _engine.Now.Add(preparationTime),
                () => RequestPushOff(train, arrivalTrack, wgClassificationMap),
                $"PreparationComplete-{train.ID}"
            );
        }

        private void RequestPushOff(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            Console.WriteLine($"  → Requesting push-off for train {train.ID}");

            var pushOffTime = TimeSpan.FromMinutes(5);

            _engine.Schedule(
                _engine.Now.Add(pushOffTime),
                () => ExecutePushOff(train, arrivalTrack, wgClassificationMap),
                $"PushOffExecuted-{train.ID}"
            );
        }

        private void ExecutePushOff(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            Console.WriteLine($"{DateTime.Now:MM/dd/yy HH:mm:ss} | {_engine.Now:yyyy-MM-ddTHH:mm:ss'Z'} | train {train.ID} push-off executed");

            if (_arrivalTrackEntryTimes.ContainsKey(train.ID))
            {
                var dwellTime = _engine.Now - _arrivalTrackEntryTimes[train.ID];
                Console.WriteLine($"  → Arrival track dwell time: {dwellTime.TotalMinutes:F2} minutes");
            }

            foreach (var wgId in train.WagonGroupIds)
            {
                if (wgClassificationMap.ContainsKey(wgId))
                {
                    var targetTrack = wgClassificationMap[wgId];
                    var wgData = _wagonGroupData[wgId];

                    _classificationControl.CreateWagonGroupEntity(wgId, wgData, targetTrack);
                }
            }

            arrivalTrack.CurrentOccupancies.Remove(train.ID);

            Console.WriteLine($"  ✗ Train entity {train.ID} destroyed after push-off");
        }
    }
}