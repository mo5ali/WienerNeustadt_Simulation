using System;
using System.Collections.Generic;
using System.Globalization;
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

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | Train {train.ID} queued at entry. Queue length: {_entryQueue.Count}");
            SimulationLogger.Instance.LogTrainEvent(
                train.ID,
                "Entry",
                _engine.Now,
                details: BuildTrainDetails(trainDto, _wagonGroupData)
);

            var firstTrain = _entryQueue.Peek();
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: handling train {firstTrain.ID} of length {firstTrain.Length} meters");

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
                            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: train {train.ID} assigned arrival track {track.RealLifeID}");
                            SimulationLogger.Instance.LogTrainEvent(train.ID, "AssignedArrivalTrack", _engine.Now, assignedTrack.RealLifeID);
                            track.Reserved = true;
                            break;
                        }
                    }
                }

                if (assignedTrack == null)
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: no arrival track currently available for train {train.ID}");

                    if (firstTime)
                    {
                        Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: starting waiting activity for train {train.ID}");
                        train.Status = "waiting for arrival track";
                        firstTime = false;
                    }

                    System.Threading.Thread.Sleep(30000);
                }
            }

            var driveActivity = new ArrivalDriveActivity(
                engine: _engine,
                trainId: train.ID,
                arrivalTrackId: assignedTrack.RealLifeID,
                area: assignedTrack.Area,
                controlUnit: "ArrivalCU",
                requestedAt: _engine.Now
            );

            var driveTime = driveActivity.CalculateFixedDuration();

            // Submitted is now emitted automatically by the Activity constructor.
            // Commencement + completion go through the base-class helpers so the
            // canonical Started / Completed rows are logged identically to every
            // other activity type.
            driveActivity.OnReadyToCommence = _ =>
            {
                driveActivity.MarkCommenced(_engine.Now);
                _engine.Schedule(
                    _engine.Now.Add(driveTime),
                    () =>
                    {
                        driveActivity.MarkCompleted(_engine.Now);
                        assignedTrack.CurrentOccupancies.Add(train.ID);
                        SimulationLogger.Instance.LogTrainEvent(train.ID, "ArrivedArrivalTrack", _engine.Now, assignedTrack.RealLifeID);
                        var wagonGroupToTrackMap = RunSortingMethod(train);
                        _trainWagonGroupMaps[train.ID] = wagonGroupToTrackMap;
                        RequestTrainPreparation(train, assignedTrack);
                    },
                    $"ArrivalDriveComplete-{train.ID}"
                );
            };

            // No resources needed — commence immediately
            driveActivity.OnReadyToCommence?.Invoke(null);

            _entryQueue.Dequeue();
        }
        static string BuildTrainDetails(TrainDto t, Dictionary<string, WagonGroupDto> wagonGroupData)
        {
            var wgIds = t.WagonGroupIds ?? new List<string>();

            double totalLen = 0;
            var wgs = new List<string>();

            foreach (var wgId in wgIds)
            {
                if (!wagonGroupData.TryGetValue(wgId, out var wg) || wg == null)
                {
                    wgs.Add($"{wgId}:0:Unknown");
                    continue;
                }

                var len = wg.Length ?? 0;
                totalLen += len;
                var dest = (wg.Destination ?? "Unknown").Replace(":", "").Replace(",", "");

                wgs.Add($"{wgId}:{len.ToString(CultureInfo.InvariantCulture)}:{dest}");
            }

            return $"trainId={t.ID}|length={totalLen.ToString(CultureInfo.InvariantCulture)}|wgIds={string.Join(",", wgIds)}|wgs={string.Join(",", wgs)}";
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

        // Called by ClassificationCU (via the DestinationCommittedToOutbound event)
        // when an OutboundTrain is created on the destination's currently-mapped
        // classification track. Releases the destination → track binding so the
        // very next WG of this destination triggers a fresh track assignment in
        // RunSortingMethod, instead of being routed onto a track that's already
        // committed to an in-progress OBT.
        //
        // The previously-mapped track stays Reserved + occupied by the OBT until
        // its DEPD completes; ClassificationCU.CompleteDepartureDrive unreserves
        // it at that point so it can be reused by any destination later.
        public void ReleaseDestinationMapping(string destination)
        {
            if (string.IsNullOrEmpty(destination)) return;
            if (!_destinationToTrackMap.ContainsKey(destination)) return;

            var releasedTrackId = _destinationToTrackMap[destination].RealLifeID;
            _destinationToTrackMap.Remove(destination);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: destination '{destination}' released from track {releasedTrackId} (OBT committed) — next {destination} WG will be sorted to a new track");
        }

        // Resolves the *current* classification track for a destination at the
        // moment a PushOffActivity sub-drive is about to fire — i.e. late
        // binding instead of relying on the sort-time wagonGroupToTrackMap
        // (which can be stale: if an OBT was committed on the previously-mapped
        // track between sort time and push time, ReleaseDestinationMapping
        // dropped the binding and the stale map would otherwise keep pushing
        // new WGs onto the now-OBT-committed track).
        //
        // Behaviour:
        //   - destination still mapped → return the mapped track unchanged.
        //   - destination not mapped (released by ReleaseDestinationMapping) →
        //     pick a free + unreserved classification track, reserve it,
        //     register the mapping, and return it.
        //   - no free track available → return null. Caller is expected to log
        //     and skip the affected sub-push.
        public Track? ResolveTrackForDestination(string destination)
        {
            if (string.IsNullOrEmpty(destination)) return null;

            if (_destinationToTrackMap.TryGetValue(destination, out var existing))
                return existing;

            Track? freshTrack = null;
            foreach (var track in _classificationTracks)
            {
                if (track.CurrentOccupancies.Count == 0 && track.Reserved == false)
                {
                    freshTrack = track;
                    track.Reserved = true;
                    break;
                }
            }

            if (freshTrack == null)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: ERROR — no free classification track for destination '{destination}' at PushOff time");
                return null;
            }

            _destinationToTrackMap[destination] = freshTrack;
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: destination '{destination}' re-assigned to track {freshTrack.RealLifeID} at PushOff time (previous track committed to OBT)");
            return freshTrack;
        }

        private Dictionary<string, Track> RunSortingMethod(Train train)
        {
            var wagonGroupToTrackMap = new Dictionary<string, Track>();
            var sortingOutputs = new List<string>();  // Collect all sorting assignments

            foreach (var wgId in train.WagonGroupIds)
            {
                if (!_wagonGroupData.ContainsKey(wgId))
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | SORTING: WARNING - wagon group {wgId} not found");
                    continue;
                }

                var wgData = _wagonGroupData[wgId];
                var destination = wgData.Destination;

                if (string.IsNullOrEmpty(destination))
                {
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | SORTING: WARNING - wagon group {wgId} has no destination");
                    continue;
                }

                bool isNewAssignment = false;

                if (!_destinationToTrackMap.ContainsKey(destination))
                {
                    Track? classificationTrack = null;

                    foreach (var track in _classificationTracks)
                    {
                        if (track.CurrentOccupancies.Count == 0 && track.Reserved == false)
                        {
                            classificationTrack = track;
                            track.Reserved = true;
                            break;
                        }
                    }

                    // No free classification track exists right now. Don't crash —
                    // log a clear error to console and CSV, skip the WG, and let
                    // the rest of the sort continue. The skipped WG won't be
                    // included in this train's PushOff (it's effectively stuck on
                    // the arrival track) and will need ops attention. This is
                    // rare — if you're hitting it often, you've either run out
                    // of classification tracks for the workload, or the
                    // classification-side Reserved flags aren't being released
                    // fast enough by departing OBTs.
                    if (classificationTrack == null)
                    {
                        Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | SORTING: ERROR — no free classification track for destination '{destination}'; WG {wgId} of train {train.ID} will be skipped");
                        SimulationLogger.Instance.LogTrainEvent(train.ID, "ClassificationTrackUnavailable", _engine.Now, $"{destination} (wg={wgId})");
                        sortingOutputs.Add($"[{wgId} → {destination} → UNASSIGNED]");
                        continue; // skip this WG, don't write a null into the map
                    }

                    _destinationToTrackMap[destination] = classificationTrack;
                    SimulationLogger.Instance.LogTrainEvent(train.ID, "ClassificationTrackAssigned", _engine.Now, $"{destination} -> {classificationTrack.RealLifeID}");

                    isNewAssignment = true;  // Mark as new
                }

                var assignedTrack = _destinationToTrackMap[destination];
                wagonGroupToTrackMap[wgId] = assignedTrack;

                // Build sorting output with asterisks for new assignments
                string trackDisplay = isNewAssignment ? $"*{assignedTrack.RealLifeID}*" : assignedTrack.RealLifeID;
                sortingOutputs.Add($"[{wgId} → {destination} → {trackDisplay}]");
            }

            // Print consolidated sorting line
            if (sortingOutputs.Count > 0)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | SORTING: {string.Join(" ", sortingOutputs)}");
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
            var allocatedWorkers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = new Dictionary<string, double>();
            foreach (var worker in allocatedWorkers)
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);

            // Duration must be computed BEFORE MarkCommenced so the Started log carries
            // the calculated duration / worker multiplier.
            var duration = activity.CalculateDuration(workerMultipliers);

            activity.MarkCommenced(_engine.Now);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: Commence '{activity.ActivityId}'");
            SimulationLogger.Instance.LogTrainEvent(train.ID, "PreparationStarted", _engine.Now);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: length={activity.EntityLength:F1}m base={activity.BaseSecondsPerMeter:F1}s/m avgMult={activity.AverageWorkerMultiplier:F2} dur={duration.TotalMinutes:F1}min");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(train, arrivalTrack, activity),
                $"ActivityComplete-{activity.ActivityId}"
            );
        }

        private void CompleteActivity(Train train, Track arrivalTrack, Activity activity)
        {
            activity.MarkCompleted(_engine.Now);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: DONE '{activity.ActivityId}' for train {train.ID}");

            if (activity.ActivityType == "IncomingTrainPreparation")
            {
                // Uniform release:
                //   ManipulationActivity (ITP): LocoStaysWithEntity=true  → loco kept in AllocatedLocoIds for hand-off ✓
                //                               WorkersReleasedIndividually=false → workers batch-returned ✓
                _resourceControl.Release(activity);

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

            // Defensive filter: if RunSortingMethod skipped any WG due to no
            // free classification track, that WG won't be in the map and would
            // KeyNotFoundException inside PushOffActivity. Drop those WGs from
            // the push-off list so the rest of the train can still be processed.
            var pushableWagonGroupIds = train.WagonGroupIds
                .Where(wgId => wagonGroupToTrackMap.ContainsKey(wgId))
                .ToList();
            if (pushableWagonGroupIds.Count < train.WagonGroupIds.Count)
            {
                var skipped = string.Join(",", train.WagonGroupIds.Where(id => !wagonGroupToTrackMap.ContainsKey(id)));
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: PushOff for train {train.ID} excludes unassigned WGs [{skipped}]");
            }

            var pushOffActivity = new PushOffActivity(
                trainId: train.ID,
                trainLength: train.Length,
                wagonGroupIds: pushableWagonGroupIds,
                fromLocation: arrivalTrack.RealLifeID,
                area: arrivalTrack.Area,
                requestedAt: _engine.Now,
                engine: _engine,
                wagonGroupData: _wagonGroupData,
                classificationControl: _classificationControl,
                // Late-binding resolver: PushOffActivity calls this for each
                // sub-drive so a destination committed to an OBT mid-PushOff
                // (or between sort time and push time) reroutes new WGs to a
                // fresh track instead of piling onto the now-committed one.
                resolveTrackForDestination: ResolveTrackForDestination
            );

            // Hand over the loco that stayed allocated on the ITP activity.
            // LocoStaysWithEntity=true on ManipulationActivity(ITP) guaranteed
            // Release() left AllocatedLocoIds intact for exactly this hand-off.
            pushOffActivity.AllocatedLocoIds.AddRange(itpActivity.AllocatedLocoIds);

            var resourceRequest = new ResourceRequest(
                activity: pushOffActivity,
                workers: pushOffActivity.RequiredWorkers,
                loco: false, // Loco already assigned above
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
                // Uniform release:
                //   PushOffActivity: LocoStaysWithEntity=false          → loco returned to pool ✓
                //                    WorkersReleasedIndividually=true   → each worker dispatched alone ✓
                _resourceControl.Release(pushOffActivity);

                arrivalTrack.CurrentOccupancies.Remove(train.ID);
                arrivalTrack.Reserved = false;
                SimulationLogger.Instance.LogTrainEvent(train.ID, "PushOffComplete", _engine.Now);
                SimulationLogger.Instance.LogTrainEvent(train.ID, "ExitedSystem", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ArrivalCU: train {train.ID} fully processed and removed from system");
            };
        }
    }
}