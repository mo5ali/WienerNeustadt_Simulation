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
    /// Manages incoming trains from entry through arrival track to push-off and dismantling.
    /// Combines entry queue management + arrival track processing.
    /// </summary>
    public class ArrivalControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ClassificationControlUnit _classificationControl;
        private readonly ResourceControlUnit _resourceControl;

        // Entry-related state (from EntryControlUnit)
        private readonly Queue<Train> _entryQueue;
        private readonly List<Track> _arrivalTracks;
        private readonly Dictionary<string, DateTime> _entryTimes;

        // Arrival track processing state
        private readonly Dictionary<string, Track> _destinationToTrackMap;
        private readonly List<Track> _classificationTracks;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;
        private readonly Dictionary<string, DateTime> _arrivalTrackEntryTimes;

        private readonly Dictionary<string, Dictionary<string, Track>> _trainWagonGroupMaps;

        public ArrivalControlUnit(
            SimulationEngine engine,
            ClassificationControlUnit classificationControl,
            ResourceControlUnit resourceControl,
            List<Track> arrivalTracks,
            List<Track> classificationTracks,
            Dictionary<string, WagonGroupDto> wagonGroupData)
        {
            _engine = engine;
            _classificationControl = classificationControl;
            _resourceControl = resourceControl;

            _arrivalTracks = arrivalTracks;
            _classificationTracks = classificationTracks;
            _wagonGroupData = wagonGroupData;

            _entryQueue = new Queue<Train>();
            _entryTimes = new Dictionary<string, DateTime>();
            _destinationToTrackMap = new Dictionary<string, Track>();
            _arrivalTrackEntryTimes = new Dictionary<string, DateTime>();

            _trainWagonGroupMaps = new Dictionary<string, Dictionary<string, Track>>();
        }

        // ============================================================
        // ENTRY PHASE
        // ============================================================

        /// <summary>
        /// Called from Program.cs when a train arrives at the system entry.
        /// </summary>
        public void HandleTrainArrival(TrainDto trainDto, DateTime simTimeUtc)
        {
            var train = CreateTrainEntity(trainDto);
            _entryQueue.Enqueue(train);
            _entryTimes[train.ID] = simTimeUtc;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | Train {train.ID} queued at entry. Queue length: {_entryQueue.Count}");

            var firstTrain = _entryQueue.Peek();
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: handling train {firstTrain.ID} of length {firstTrain.Length} meters");

            Track assignedTrack = null;
            bool firstTime = true;

            while (assignedTrack == null)
            {
                foreach (var track in _arrivalTracks)
                {
                    if (track.Length >= train.Length && track.Designation == "Arrival")
                    {
                        if (track.CurrentOccupancies.Count == 0 && track.Reserved == false)
                        {
                            assignedTrack = track;
                            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} assigned arrival track {track.RealLifeID} ");
                            track.Reserved = true;
                            break;
                        }
                    }
                }

                if (assignedTrack == null)
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: no arrival track currently available for train {train.ID}");

                    if (firstTime)
                    {
                        Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: starting waiting activity for train {train.ID}");
                        train.Status = "waiting for arrival track";
                        firstTime = false;
                    }

                    // NOTE: this sleep blocks the whole sim thread; consider replacing with _engine.Schedule retry later.
                    System.Threading.Thread.Sleep(30000);
                }
            }

            var driveTime = TimeSpan.FromMinutes(3);

            _engine.Schedule(
                _engine.Now.Add(driveTime),
                () =>
                {
                    assignedTrack.CurrentOccupancies.Add(train.ID);
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} arrives at arrival track {assignedTrack.RealLifeID}, and starts waiting for preparation");

                    var wagonGroupToTrackMap = RunSortingMethod(train);
                    _trainWagonGroupMaps[train.ID] = wagonGroupToTrackMap;

                    RequestTrainPreparation(train, assignedTrack);
                },
                $"TrainArrivesAtArrivalTrack-{train.ID}"
            );

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} driving to arrival track {assignedTrack.RealLifeID} (ETA: {driveTime.TotalMinutes} minutes)");
            _entryQueue.Dequeue();
        }

        private Train CreateTrainEntity(TrainDto trainDto)
        {
            double calculatedLength = 0;
            if (trainDto.WagonGroupIds != null)
            {
                foreach (var wgId in trainDto.WagonGroupIds)
                {
                    if (_wagonGroupData.ContainsKey(wgId))
                        calculatedLength += _wagonGroupData[wgId].Length ?? 0;
                }
            }

            var train = new Train(trainDto.ID ?? "00000", calculatedLength)
            {
                WagonGroupIds = trainDto.WagonGroupIds ?? new List<string>(),
                HasLoco = trainDto.HasLoco ?? false,
                LocomotiveId = trainDto.LocomotiveId ?? string.Empty,
                Status = trainDto.Status ?? string.Empty,
                Designation = trainDto.Designation ?? "Inbound"
            };

            if (DateTime.TryParse(trainDto.Time, out var parsedTime))
                train.Time = parsedTime;

            return train;
        }

        private Dictionary<string, Track> RunSortingMethod(Train train)
        {
            var wagonGroupToTrackMap = new Dictionary<string, Track>();

            foreach (var wgId in train.WagonGroupIds)
            {
                if (!_wagonGroupData.ContainsKey(wgId))
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: WARNING - wagon group {wgId} not found");
                    continue;
                }

                var wgData = _wagonGroupData[wgId];
                var destination = wgData.Destination ?? "Unknown";

                if (!_destinationToTrackMap.ContainsKey(destination))
                {
                    Track classificationTrack = null;

                    foreach (var track in _classificationTracks)
                    {
                        if (track.Designation == "Classification" && !_destinationToTrackMap.ContainsValue(track))
                        {
                            classificationTrack = track;
                            break;
                        }
                    }

                    if (classificationTrack == null)
                        Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: no free classification tracks");

                    _destinationToTrackMap[destination] = classificationTrack;
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: track {classificationTrack.RealLifeID} set for '{destination}'");
                }

                var assignedTrack = _destinationToTrackMap[destination];
                wagonGroupToTrackMap[wgId] = assignedTrack;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: wagon group {wgId} → destination '{destination}' → track {assignedTrack.RealLifeID}");
            }

            return wagonGroupToTrackMap;
        }

        private void RequestTrainPreparation(Train train, Track arrivalTrack)
        {
            // This request defines requirements + baseSecondsPerMeter.
            // ResourceCU will allocate + schedule worker travel, then call OnFulfilled ONLY when all arrived.
            var prepReq = new TrainPreparationRequest(train.ID, train.Length, arrivalTrack.RealLifeID, arrivalTrack.Area);

            prepReq.OnFulfilled = _ =>
            {
                // Resources arrived; commence now.
                CommenceAndScheduleActivity(train, arrivalTrack, prepReq);
            };

            _resourceControl.Submit(prepReq);
        }

        private void CommenceAndScheduleActivity(Train train, Track arrivalTrack, ResourceRequest req)
        {
            req.CommencedAtUtc = _engine.Now;

            var allocatedWorkers = _resourceControl.GetWorkersByIds(req.AllocatedWorkerIds);

            double avgMultiplier = 1.0;
            if (allocatedWorkers.Count > 0)
            {
                avgMultiplier = allocatedWorkers
                    .Select(w => _resourceControl.GetWorkerTimeMultiplierForActivity(w, req.ActivityTypeKey))
                    .Average();
            }

            var durationSeconds = req.EntityLengthMeters * req.BaseSecondsPerMeter * avgMultiplier;
            if (durationSeconds < 0) durationSeconds = 0;

            var duration = TimeSpan.FromSeconds(durationSeconds);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: COMMENCE '{req.ForActivity}' for train {train.ID}");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: length={req.EntityLengthMeters:F1}m base={req.BaseSecondsPerMeter:F1}s/m avgMult={avgMultiplier:F2} -> duration={duration.TotalSeconds:F0}s");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(train, arrivalTrack, req),
                $"ActivityComplete-{req.RequestId}"
            );
        }

        private void CompleteActivity(Train train, Track arrivalTrack, ResourceRequest req)
        {
            req.FinishedAtUtc = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: DONE '{req.ForActivity}' for train {train.ID}");

            _resourceControl.Release(req);

            // TODO: Next step - request pushoff using same pattern (PushOffRequest)
        }
    }
}