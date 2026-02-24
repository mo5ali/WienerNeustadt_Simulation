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
    /// Manages trains on arrival tracks: waiting, preparation+sorting, push-off, dismantling.
    /// Sequence aligned with provided flowcharts.
    /// </summary>
    public class ArrivalControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ClassificationControlUnit _classificationControl;
        private readonly ResourceControlUnit _resourceControl;

        private readonly Dictionary<string, Track> _destinationToTrackMap;
        private readonly List<Track> _classificationTracks;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;
        private readonly Dictionary<string, DateTime> _arrivalTrackEntryTimes;

        public ArrivalControlUnit(
            SimulationEngine engine,
            ClassificationControlUnit classificationControl,
            ResourceControlUnit resourceControl,
            List<Track> classificationTracks,
            Dictionary<string, WagonGroupDto> wagonGroupData)
        {
            _engine = engine;
            _classificationControl = classificationControl;
            _resourceControl = resourceControl;

            _classificationTracks = classificationTracks;
            _wagonGroupData = wagonGroupData;

            _destinationToTrackMap = new Dictionary<string, Track>();
            _arrivalTrackEntryTimes = new Dictionary<string, DateTime>();
        }

        public void HandleTrainOnArrivalTrack(Train train, Track arrivalTrack)
        {
            _arrivalTrackEntryTimes[train.ID] = _engine.Now;

            Console.WriteLine($"  → Train {train.ID} arrived on arrival track {arrivalTrack.StationID}");
            Console.WriteLine($"  → Start waiting activity (arrival track) for train {train.ID}");

            // Flowchart has a waiting activity before preparation/sorting begins.
            var waitTime = TimeSpan.FromMinutes(2);
            _engine.Schedule(
                _engine.Now.Add(waitTime),
                () => RequestIncomingTrainPreparationAndSorting(train, arrivalTrack),
                $"ArrivalWaitComplete-{train.ID}"
            );
        }

        private void RequestIncomingTrainPreparationAndSorting(Train train, Track arrivalTrack)
        {
            // 1) Sorting method: decide classification track mapping.
            var wgClassificationMap = RunSortingMethod(train);

            // 2) Resource request for incoming train preparation (blocks this train until fulfilled).
            var prepReq = new TrainPreparationRequest(train.ID, arrivalTrack.StationID, arrivalTrack.Area);

            prepReq.OnFulfilled = _ =>
            {
                // Once resources are assigned, start the preparation activity.
                StartIncomingTrainPreparationActivity(train, arrivalTrack, wgClassificationMap, prepReq);
            };

            _resourceControl.Submit(prepReq);

            // IMPORTANT: we do NOT schedule next steps here. The chain resumes in OnFulfilled.
        }

        /// <summary>
        /// This is the "Sorting method" from your diagram:
        /// map each wagon group destination to a classification track.
        /// </summary>
        private Dictionary<string, Track> RunSortingMethod(Train train)
        {
            Console.WriteLine($"  → Run sorting method for train {train.ID}");

            var wgClassificationMap = new Dictionary<string, Track>();

            foreach (var wgId in train.WagonGroupIds)
            {
                if (!_wagonGroupData.ContainsKey(wgId))
                {
                    Console.WriteLine($"  ⚠ Warning: WagonGroup {wgId} not found in input data");
                    continue;
                }

                var wgData = _wagonGroupData[wgId];
                var destination = wgData.Destination ?? "Unknown";

                if (!_destinationToTrackMap.ContainsKey(destination))
                {
                    var assignedTrack = AllocateClassificationTrack(destination);
                    _destinationToTrackMap[destination] = assignedTrack;
                    Console.WriteLine($"  → Destination '{destination}' mapped to track {assignedTrack.StationID}");
                }

                wgClassificationMap[wgId] = _destinationToTrackMap[destination];
                Console.WriteLine($"  → WG {wgId} → Destination '{destination}' → Track {_destinationToTrackMap[destination].StationID}");
            }

            return wgClassificationMap;
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

                Console.WriteLine($"  ⚠ No free classification tracks - reusing track {availableTrack.StationID} for destination '{destination}'");
            }

            return availableTrack;
        }

        private void StartIncomingTrainPreparationActivity(
            Train train,
            Track arrivalTrack,
            Dictionary<string, Track> wgClassificationMap,
            ResourceRequest prepReq)
        {
            Console.WriteLine($"  → Incoming train preparation START (activity: {prepReq.ForActivity}) for train {train.ID}");

            // "Preparation of incoming train" (done to the entity) duration:
            var preparationTime = TimeSpan.FromMinutes(10);

            _engine.Schedule(
                _engine.Now.Add(preparationTime),
                () => CompleteIncomingTrainPreparation(train, arrivalTrack, wgClassificationMap, prepReq),
                $"IncomingTrainPreparationComplete-{train.ID}"
            );
        }

        private void CompleteIncomingTrainPreparation(
            Train train,
            Track arrivalTrack,
            Dictionary<string, Track> wgClassificationMap,
            ResourceRequest prepReq)
        {
            Console.WriteLine($"  ✓ Incoming train preparation DONE for train {train.ID}");

            // Release prep resources now that this activity is finished
            _resourceControl.Release(prepReq);

            // Next: Request PushOff (blocks this train until fulfilled)
            RequestPushOff(train, arrivalTrack, wgClassificationMap);
        }

        private void RequestPushOff(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        {
            Console.WriteLine($"  → Request PushOff for train {train.ID}");

            var poReq = new PushOffRequest(train.ID, arrivalTrack.StationID, arrivalTrack.Area);

            poReq.OnFulfilled = _ =>
            {
                StartPushOffProcess(train, arrivalTrack, wgClassificationMap, poReq);
            };

            _resourceControl.Submit(poReq);
        }

        private void StartPushOffProcess(
            Train train,
            Track arrivalTrack,
            Dictionary<string, Track> wgClassificationMap,
            ResourceRequest poReq)
        {
            Console.WriteLine($"  → Push-off process START (activity: {poReq.ForActivity}) for train {train.ID}");

            var pushOffTime = TimeSpan.FromMinutes(5);

            _engine.Schedule(
                _engine.Now.Add(pushOffTime),
                () => CompletePushOffAndDismantle(train, arrivalTrack, wgClassificationMap, poReq),
                $"PushOffComplete-{train.ID}"
            );
        }

        private void CompletePushOffAndDismantle(
            Train train,
            Track arrivalTrack,
            Dictionary<string, Track> wgClassificationMap,
            ResourceRequest poReq)
        {
            Console.WriteLine($"{DateTime.Now:MM/dd/yy HH:mm:ss} | {_engine.Now:yyyy-MM-ddTHH:mm:ss'Z'} | train {train.ID} push-off executed");

            // Release push-off resources
            _resourceControl.Release(poReq);

            if (_arrivalTrackEntryTimes.ContainsKey(train.ID))
            {
                var dwellTime = _engine.Now - _arrivalTrackEntryTimes[train.ID];
                Console.WriteLine($"  → Arrival track dwell time: {dwellTime.TotalMinutes:F2} minutes");
            }

            // Dismantle train into WGs and place them on classification tracks
            foreach (var wgId in train.WagonGroupIds)
            {
                if (!wgClassificationMap.ContainsKey(wgId))
                    continue;

                if (!_wagonGroupData.ContainsKey(wgId))
                    continue;

                var targetTrack = wgClassificationMap[wgId];
                var wgData = _wagonGroupData[wgId];

                _classificationControl.CreateWagonGroupEntity(wgId, wgData, targetTrack);
            }

            arrivalTrack.CurrentOccupancies.Remove(train.ID);

            Console.WriteLine($"  ✗ Train entity {train.ID} removed (dismantled into WGs)");
        }
    }
}