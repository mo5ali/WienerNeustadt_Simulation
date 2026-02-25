using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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
        }

        // ============================================================
        // ENTRY PHASE
        // ============================================================

        /// <summary>
        /// Called from Program.cs when a train arrives at the system entry.
        /// </summary>
        public void HandleTrainArrival(TrainDto trainDto, DateTime simTimeUtc)
        {
            Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | train {trainDto.ID} arrives at entry");

            var train = CreateTrainEntity(trainDto);
            _entryQueue.Enqueue(train);
            _entryTimes[train.ID] = simTimeUtc;

            Console.WriteLine($"  → Train {train.ID} queued at entry. Queue length: {_entryQueue.Count}");

            // Print handling first train in queue
            var firstTrain = _entryQueue.Peek();
            Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: handling train {firstTrain.ID} of length {firstTrain.Length} meters");

            Track assignedTrack = null;
            bool firstTime = true;

            
            while (assignedTrack == null) // Loop until we find a track
            {
                foreach (var track in _arrivalTracks)// Look for a long enough, available arrival tracks
                {
                    if (track.Length >= train.Length && track.Designation == "Arrival")
                    {
                        // Check if track is free
                        if (track.CurrentOccupancies.Count == 0 && track.Reserved == false)
                        {
                            assignedTrack = track;
                            Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} assigned arrival track {track.StationID} ");
                            track.Reserved = true;
                            break; // Exit the foreach loop
                        }
                    }
                }

                // If no track found
                if (assignedTrack == null)
                {
                    Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: no arrival track currently available for train {train.ID}");

                    if (firstTime)
                    {
                        Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: starting waiting activity for train {train.ID}");
                        train.Status = "waiting for arrival track";
                        firstTime = false;
                    }
                    System.Threading.Thread.Sleep(30000); // Wait 30 seconds before trying again
                }
            }

            // Schedule a driving activity to the destination track
            var driveTime = TimeSpan.FromMinutes(3);  // Simple: 3 minutes to drive there

            _engine.Schedule(
                _engine.Now.Add(driveTime),
                () =>
                {
                    // Train arrives at arrival track
                    assignedTrack.CurrentOccupancies.Add(train.ID);
                    Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} arrives at arrival track {assignedTrack.StationID}");
                },
                $"TrainArrivesAtArrivalTrack-{train.ID}"
            );

            Console.WriteLine($"{simTimeUtc:dd/MM/yyyy-HH:mm:ss} | ArrivalCU: train {train.ID} driving to arrival track {assignedTrack.StationID} (ETA: {driveTime.TotalMinutes} minutes)");
            _entryQueue.Dequeue();


        }

        private Train CreateTrainEntity(TrainDto trainDto)
        {
            var train = new Train(trainDto.ID ?? "00000", trainDto.Length ?? 0)
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

        ///// <summary>
        ///// Searches for an available arrival track that can accommodate the train.
        ///// Returns null if no suitable track is found.
        ///// </summary>
        //private Track? SearchForArrivalTrack(Train train)
        //{
        //    return _arrivalTracks
        //        .Where(t => t.Length >= train.Length && t.Designation == "Arrival")
        //        .OrderBy(t => t.CurrentOccupancies.Count)
        //        .FirstOrDefault();
        //}

        //private bool IsTrackFree(Track track)
        //{
        //    return track.CurrentOccupancies.Count == 0;
        //}

        //private void StartWaitingForArrivalTrack(Train train)
        //{
        //    Console.WriteLine($"  → Train {train.ID} waiting for arrival track (retry in 30s)");

        //    _engine.Schedule(
        //        _engine.Now.AddSeconds(30),
        //        () => RetryArrivalTrackSearch(train),
        //        $"RetryArrivalTrack-{train.ID}"
        //    );
        //}

        //private void RetryArrivalTrackSearch(Train train)
        //{
        //    var availableTrack = SearchForArrivalTrack(train);

        //    if (availableTrack != null && IsTrackFree(availableTrack))
        //    {
        //        AssignToArrivalTrack(train, availableTrack);
        //    }
        //    else
        //    {
        //        // Still no track available - retry again
        //        StartWaitingForArrivalTrack(train);
        //    }
        //}

        //private void AssignToArrivalTrack(Train train, Track arrivalTrack)
        //{
        //    Console.WriteLine($"  → Train {train.ID} assigned to arrival track {arrivalTrack.StationID}");

        //    if (_entryTimes.ContainsKey(train.ID))
        //    {
        //        var queuingTime = _engine.Now - _entryTimes[train.ID];
        //        Console.WriteLine($"  → Queuing time: {queuingTime.TotalMinutes:F2} minutes");
        //    }

        //    var driveTime = CalculateDriveTime(train, arrivalTrack);

        //    _engine.Schedule(
        //        _engine.Now.Add(driveTime),
        //        () => ArriveAtArrivalTrack(train, arrivalTrack),
        //        $"ArriveAtArrivalTrack-{train.ID}"
        //    );

        //    Console.WriteLine($"  → Train {train.ID} driving to arrival track (ETA: {driveTime.TotalMinutes:F1} min)");
        //}

        //private TimeSpan CalculateDriveTime(Train train, Track arrivalTrack)
        //{
        //    // Simplified drive time calculation
        //    return TimeSpan.FromMinutes(3);
        //}

        //private void ArriveAtArrivalTrack(Train train, Track arrivalTrack)
        //{
        //    Console.WriteLine($"  ✓ Train {train.ID} arrived at arrival track {arrivalTrack.StationID}");

        //    arrivalTrack.CurrentOccupancies.Add(train.ID);

        //    // Now proceed to arrival track processing phase
        //    HandleTrainOnArrivalTrack(train, arrivalTrack);
        //}

        //// ============================================================
        //// ARRIVAL TRACK PHASE (existing logic, refactored)
        //// ============================================================

        //private void HandleTrainOnArrivalTrack(Train train, Track arrivalTrack)
        //{
        //    _arrivalTrackEntryTimes[train.ID] = _engine.Now;

        //    Console.WriteLine($"  → Train {train.ID} on arrival track {arrivalTrack.StationID}");
        //    Console.WriteLine($"  → Start waiting activity (arrival track) for train {train.ID}");

        //    // Flowchart: waiting activity before preparation/sorting begins.
        //    var waitTime = TimeSpan.FromMinutes(2);
        //    _engine.Schedule(
        //        _engine.Now.Add(waitTime),
        //        () => RequestIncomingTrainPreparationAndSorting(train, arrivalTrack),
        //        $"ArrivalWaitComplete-{train.ID}"
        //    );
        //}

        //private void RequestIncomingTrainPreparationAndSorting(Train train, Track arrivalTrack)
        //{
        //    // 1) Sorting method: decide classification track mapping.
        //    var wgClassificationMap = RunSortingMethod(train);

        //    // 2) Resource request for incoming train preparation (blocks this train until fulfilled).
        //    var prepReq = new TrainPreparationRequest(train.ID, arrivalTrack.StationID, arrivalTrack.Area);

        //    prepReq.OnFulfilled = _ =>
        //    {
        //        // Once resources are assigned, start the preparation activity.
        //        StartIncomingTrainPreparationActivity(train, arrivalTrack, wgClassificationMap, prepReq);
        //    };

        //    _resourceControl.Submit(prepReq);
        //}

        ///// <summary>
        ///// Sorting method: map each wagon group destination to a classification track.
        ///// </summary>
        //private Dictionary<string, Track> RunSortingMethod(Train train)
        //{
        //    Console.WriteLine($"  → Run sorting method for train {train.ID}");

        //    var wgClassificationMap = new Dictionary<string, Track>();

        //    foreach (var wgId in train.WagonGroupIds)
        //    {
        //        if (!_wagonGroupData.ContainsKey(wgId))
        //        {
        //            Console.WriteLine($"  ⚠ Warning: WagonGroup {wgId} not found in input data");
        //            continue;
        //        }

        //        var wgData = _wagonGroupData[wgId];
        //        var destination = wgData.Destination ?? "Unknown";

        //        if (!_destinationToTrackMap.ContainsKey(destination))
        //        {
        //            var assignedTrack = AllocateClassificationTrack(destination);
        //            _destinationToTrackMap[destination] = assignedTrack;
        //            Console.WriteLine($"  → Destination '{destination}' mapped to track {assignedTrack.StationID}");
        //        }

        //        wgClassificationMap[wgId] = _destinationToTrackMap[destination];
        //        Console.WriteLine($"  → WG {wgId} → Destination '{destination}' → Track {_destinationToTrackMap[destination].StationID}");
        //    }

        //    return wgClassificationMap;
        //}

        //private Track AllocateClassificationTrack(string destination)
        //{
        //    var availableTrack = _classificationTracks
        //        .Where(t => t.Designation == "Classification")
        //        .Where(t => !_destinationToTrackMap.ContainsValue(t))
        //        .FirstOrDefault();

        //    if (availableTrack == null)
        //    {
        //        availableTrack = _classificationTracks
        //            .Where(t => t.Designation == "Classification")
        //            .OrderBy(t => t.CurrentOccupancies.Count)
        //            .First();

        //        Console.WriteLine($"  �� No free classification tracks - reusing track {availableTrack.StationID} for destination '{destination}'");
        //    }

        //    return availableTrack;
        //}

        //private void StartIncomingTrainPreparationActivity(
        //    Train train,
        //    Track arrivalTrack,
        //    Dictionary<string, Track> wgClassificationMap,
        //    ResourceRequest prepReq)
        //{
        //    Console.WriteLine($"  → Incoming train preparation START (activity: {prepReq.ForActivity}) for train {train.ID}");

        //    var preparationTime = TimeSpan.FromMinutes(10);

        //    _engine.Schedule(
        //        _engine.Now.Add(preparationTime),
        //        () => CompleteIncomingTrainPreparation(train, arrivalTrack, wgClassificationMap, prepReq),
        //        $"IncomingTrainPreparationComplete-{train.ID}"
        //    );
        //}

        //private void CompleteIncomingTrainPreparation(
        //    Train train,
        //    Track arrivalTrack,
        //    Dictionary<string, Track> wgClassificationMap,
        //    ResourceRequest prepReq)
        //{
        //    Console.WriteLine($"  ✓ Incoming train preparation DONE for train {train.ID}");

        //    _resourceControl.Release(prepReq);

        //    RequestPushOff(train, arrivalTrack, wgClassificationMap);
        //}

        //private void RequestPushOff(Train train, Track arrivalTrack, Dictionary<string, Track> wgClassificationMap)
        //{
        //    Console.WriteLine($"  → Request PushOff for train {train.ID}");

        //    var poReq = new PushOffRequest(train.ID, arrivalTrack.StationID, arrivalTrack.Area);

        //    poReq.OnFulfilled = _ =>
        //    {
        //        StartPushOffProcess(train, arrivalTrack, wgClassificationMap, poReq);
        //    };

        //    _resourceControl.Submit(poReq);
        //}

        //private void StartPushOffProcess(
        //    Train train,
        //    Track arrivalTrack,
        //    Dictionary<string, Track> wgClassificationMap,
        //    ResourceRequest poReq)
        //{
        //    Console.WriteLine($"  → Push-off process START (activity: {poReq.ForActivity}) for train {train.ID}");

        //    var pushOffTime = TimeSpan.FromMinutes(5);

        //    _engine.Schedule(
        //        _engine.Now.Add(pushOffTime),
        //        () => CompletePushOffAndDismantle(train, arrivalTrack, wgClassificationMap, poReq),
        //        $"PushOffComplete-{train.ID}"
        //    );
        //}

        //private void CompletePushOffAndDismantle(
        //    Train train,
        //    Track arrivalTrack,
        //    Dictionary<string, Track> wgClassificationMap,
        //    ResourceRequest poReq)
        //{
        //    Console.WriteLine($"{DateTime.Now:MM/dd/yy HH:mm:ss} | {_engine.Now:yyyy-MM-ddTHH:mm:ss'Z'} | train {train.ID} push-off executed");

        //    _resourceControl.Release(poReq);

        //    if (_arrivalTrackEntryTimes.ContainsKey(train.ID))
        //    {
        //        var dwellTime = _engine.Now - _arrivalTrackEntryTimes[train.ID];
        //        Console.WriteLine($"  → Arrival track dwell time: {dwellTime.TotalMinutes:F2} minutes");
        //    }

        //    // Dismantle train into WGs and place them on classification tracks
        //    foreach (var wgId in train.WagonGroupIds)
        //    {
        //        if (!wgClassificationMap.ContainsKey(wgId))
        //            continue;

        //        if (!_wagonGroupData.ContainsKey(wgId))
        //            continue;

        //        var targetTrack = wgClassificationMap[wgId];
        //        var wgData = _wagonGroupData[wgId];

        //        _classificationControl.CreateWagonGroupEntity(wgId, wgData, targetTrack);
        //    }

        //    arrivalTrack.CurrentOccupancies.Remove(train.ID);

        //    Console.WriteLine($"  ✗ Train entity {train.ID} removed (dismantled into WGs)");
        //}
    }
}