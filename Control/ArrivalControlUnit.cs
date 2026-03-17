using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Infrastructure;
using WienerNeustadtSimulation.Models;
using WienerNeustadtSimulation.Output;

namespace WienerNeustadtSimulation.Control
{
    public class ArrivalControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ClassificationControlUnit _classificationControl;
        private readonly ResourceControlUnit _resourceControl;

        private readonly Queue<Train> _entryQueue;
        private readonly List<Track> _arrivalTracks;
        private readonly Dictionary<string, DateTime> _entryTimes;

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

        public void HandleTrainArrival(TrainDto trainDto, DateTime simTimeUtc)
        {
            var train = CreateTrainEntity(trainDto);
            _entryQueue.Enqueue(train);
            _entryTimes[train.ID] = simTimeUtc;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | Train {train.ID} queued at entry. Queue length: {_entryQueue.Count}");
            SimulationLogger.Instance.LogTrainEvent(train.ID, "Entry", simTimeUtc);

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
                            SimulationLogger.Instance.LogTrainEvent(train.ID, "AssignedArrivalTrack", _engine.Now, assignedTrack.RealLifeID);
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

                    System.Threading.Thread.Sleep(30000);
                }
            }

            var driveTime = TimeSpan.FromMinutes(3);

            _engine.Schedule(
                _engine.Now.Add(driveTime),
                () =>
                {
                    assignedTrack.CurrentOccupancies.Add(train.ID);
                    SimulationLogger.Instance.LogTrainEvent(train.ID, "ArrivedArrivalTrack", _engine.Now, assignedTrack.RealLifeID);
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} arrives at arrival track {assignedTrack.RealLifeID}");

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

                    _destinationToTrackMap[destination] = classificationTrack!;
                    SimulationLogger.Instance.LogTrainEvent(train.ID, "ClassificationTrackAssigned", _engine.Now, $"{destination} -> {classificationTrack.RealLifeID}");
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: track {classificationTrack!.RealLifeID} set for '{destination}'");
                }

                var assignedTrack = _destinationToTrackMap[destination];
                wagonGroupToTrackMap[wgId] = assignedTrack;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | SORTING: wagon group {wgId} → destination '{destination}' → track {assignedTrack.RealLifeID}");
            }

            return wagonGroupToTrackMap;
        }

        private void RequestTrainPreparation(Train train, Track arrivalTrack)
        {
            var prepActivity = new ManipulationActivity(
                activityType: "IncomingTrainPreparation",
                entityId: train.ID,
                entityLength: train.Length,
                location: arrivalTrack.RealLifeID,
                area: arrivalTrack.Area,
                controlUnit: "ArrivalCU",
                requestedAt: _engine.Now
            );

            var resourceRequest = new ResourceRequest(
                activity: prepActivity,
                workers: prepActivity.RequiredWorkers,
                loco: prepActivity.RequiresLocomotive,
                time: _engine.Now,
                controlUnit: "ArrivalCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);
            SimulationLogger.Instance.LogTrainEvent(train.ID, "PreparationRequested", _engine.Now);

            prepActivity.OnReadyToCommence = _ =>
            {
                CommenceAndScheduleActivity(train, arrivalTrack, prepActivity);
            };
        }

        private void CommenceAndScheduleActivity(Train train, Track arrivalTrack, Activity activity)
        {
            activity.CommencedAt = _engine.Now;

            var allocatedWorkers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);

            var workerMultipliers = new Dictionary<string, double>();
            foreach (var worker in allocatedWorkers)
            {
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);
            }

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: Commence '{activity.ActivityId}'");
            SimulationLogger.Instance.LogTrainEvent(train.ID, "PreparationStarted", _engine.Now);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: length={activity.EntityLength:F1}m base={activity.BaseSecondsPerMeter:F1}s/m avgMult={activity.AverageWorkerMultiplier:F2} -> duration={duration.TotalSeconds:F0}s");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(train, arrivalTrack, activity),
                $"ActivityComplete-{activity.ActivityId}"
            );
        }

        private void CompleteActivity(Train train, Track arrivalTrack, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: DONE '{activity.ActivityId}' for train {train.ID}");

            if (activity.ActivityType == "IncomingTrainPreparation")
            {
                // Batch return workers using new method
                _resourceControl.ReturnWorkers(activity.AllocatedWorkerIds.ToList());
                activity.AllocatedWorkerIds.Clear();

                RequestPushOff(train, arrivalTrack, activity);
                SimulationLogger.Instance.LogTrainEvent(train.ID, "PreparationComplete", _engine.Now);
            }
            else
            {
                _resourceControl.Release(activity);
            }
        }

        private void RequestPushOff(Train train, Track arrivalTrack, Activity itpActivity)
        {
            var wagonGroupToTrackMap = _trainWagonGroupMaps.ContainsKey(train.ID)
                ? _trainWagonGroupMaps[train.ID]
                : new Dictionary<string, Track>();

            // CREATE PUSHOFF ACTIVITY WITH CLASSIFICATIONCU REFERENCE
            var pushOffActivity = new PushOffActivity(
                trainId: train.ID,
                trainLength: train.Length,
                wagonGroupIds: train.WagonGroupIds,
                wagonGroupDestinations: wagonGroupToTrackMap,
                fromLocation: arrivalTrack.RealLifeID,
                area: arrivalTrack.Area,
                requestedAt: _engine.Now,
                engine: _engine,
                wagonGroupData: _wagonGroupData,
                classificationControl: _classificationControl  // ← PASS IT HERE!
            );

            pushOffActivity.AllocatedLocoIds.AddRange(itpActivity.AllocatedLocoIds);

            var resourceRequest = new ResourceRequest(
                activity: pushOffActivity,
                workers: pushOffActivity.RequiredWorkers,
                loco: false,
                time: _engine.Now,
                controlUnit: "ArrivalCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);
            SimulationLogger.Instance.LogTrainEvent(train.ID, "PushOffRequested", _engine.Now);

            pushOffActivity.OnReadyToCommence = _ =>
            {
                pushOffActivity.CommencePushOff();
            };

            pushOffActivity.OnCompleted = _ =>
            {
                foreach (var workerId in pushOffActivity.AllocatedWorkerIds)
                {
                    var worker = _resourceControl.GetWorkersByIds(new[] { workerId }).FirstOrDefault();
                    string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {firstName} returned to pool");
                    _resourceControl.ReturnWorker(workerId);
                }
                pushOffActivity.AllocatedWorkerIds.Clear();

                if (pushOffActivity.AllocatedLocoIds.Count > 0)
                {
                    var locoId = pushOffActivity.AllocatedLocoIds[0];
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} remains in yard");
                }

                arrivalTrack.CurrentOccupancies.Remove(train.ID);
                arrivalTrack.Reserved = false;
                SimulationLogger.Instance.LogTrainEvent(train.ID, "PushOffComplete", _engine.Now);
                SimulationLogger.Instance.LogTrainEvent(train.ID, "ExitedSystem", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} fully processed and removed from system");
            };
        }
    }
}