using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Infrastructure;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Control
{
    public class ClassificationControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ResourceControlUnit _resourceControl;
        private readonly List<Track> _classificationTracks;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;

        // Track wagon groups and trains by classification track
        private readonly Dictionary<string, List<WagonGroup>> _wagonGroupsByTrack;
        private readonly Dictionary<string, OutboundTrain> _trainsByTrack;

        // Request queues
        private readonly Queue<WagonGroupPreparationRequest> _preparationRequests;
        private readonly Queue<CompletionCheckRequest> _completionCheckRequests;
        private readonly Queue<TrainDepartureRequest> _departureRequests;

        // Completion criteria
        private const double MIN_TRAIN_LENGTH = 100.0;

        public ClassificationControlUnit(
            SimulationEngine engine,
            ResourceControlUnit resourceControl,
            List<Track> classificationTracks,
            Dictionary<string, WagonGroupDto> wagonGroupData)
        {
            _engine = engine;
            _resourceControl = resourceControl;
            _classificationTracks = classificationTracks;
            _wagonGroupData = wagonGroupData;

            _wagonGroupsByTrack = new Dictionary<string, List<WagonGroup>>();
            _trainsByTrack = new Dictionary<string, OutboundTrain>();

            _preparationRequests = new Queue<WagonGroupPreparationRequest>();
            _completionCheckRequests = new Queue<CompletionCheckRequest>();
            _departureRequests = new Queue<TrainDepartureRequest>();

            Console.WriteLine($"  → ClassificationControlUnit: managing {_classificationTracks.Count} classification tracks");
        }

        // Called by PushOffActivity when wagon group arrives
        public void HandleWagonGroupArrival(WagonGroup wagonGroup, Track classificationTrack, DateTime time)
        {
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: {wagonGroup} arrived at track {classificationTrack.RealLifeID}");

            // Track wagon group
            if (!_wagonGroupsByTrack.ContainsKey(classificationTrack.RealLifeID))
                _wagonGroupsByTrack[classificationTrack.RealLifeID] = new List<WagonGroup>();

            _wagonGroupsByTrack[classificationTrack.RealLifeID].Add(wagonGroup);

            // File preparation request
            var request = new WagonGroupPreparationRequest(wagonGroup, time);
            _preparationRequests.Enqueue(request);

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: filed WagonGroupPreparationRequest for {wagonGroup.Id}");

            // Process requests immediately
            ProcessRequests(time);
        }

        // Process all pending requests
        public void ProcessRequests(DateTime time)
        {
            // Process preparation requests
            while (_preparationRequests.Count > 0)
            {
                var request = _preparationRequests.Dequeue();
                HandleWagonGroupPreparation(request, time);
            }

            // Process completion checks
            while (_completionCheckRequests.Count > 0)
            {
                var request = _completionCheckRequests.Dequeue();
                HandleCompletionCheck(request, time);
            }

            // Process departure requests
            while (_departureRequests.Count > 0)
            {
                var request = _departureRequests.Dequeue();
                HandleTrainDeparture(request, time);
            }
        }

        private void HandleWagonGroupPreparation(WagonGroupPreparationRequest request, DateTime time)
        {
            var wagonGroup = request.WagonGroup;
            var trackId = wagonGroup.CurrentTrackId;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: processing WagonGroupPreparationRequest for {wagonGroup.Id}");

            // Check if track is empty (only this wagon group)
            bool isTrackEmpty = _wagonGroupsByTrack[trackId].Count == 1;

            Activity activity;
            if (isTrackEmpty)
            {
                // Track is empty → SECURE wagon group
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: track {trackId} is EMPTY → requesting SECURING");

                activity = new SecuringActivity(
                    wagonGroupId: wagonGroup.Id,
                    wagonGroupLength: wagonGroup.Length,
                    location: trackId,
                    area: wagonGroup.CurrentArea,
                    controlUnit: "ClassificationCU",
                    requestedAt: time
                );
            }
            else
            {
                // Track has wagon groups → COUPLE to existing
                var existingTrain = _trainsByTrack.ContainsKey(trackId) ? _trainsByTrack[trackId] : null;
                string couplingToId = existingTrain?.Id ?? "existing-wgs";

                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: track {trackId} has wagon groups → requesting COUPLING to {couplingToId}");

                activity = new CouplingActivity(
                    wagonGroupId: wagonGroup.Id,
                    wagonGroupLength: wagonGroup.Length,
                    couplingToTrainId: couplingToId,
                    location: trackId,
                    area: wagonGroup.CurrentArea,
                    controlUnit: "ClassificationCU",
                    requestedAt: time
                );
            }

            // Create and submit resource request
            var resourceRequest = new ResourceRequest(
                activity: activity,
                workers: activity.RequiredWorkers,
                loco: activity.RequiresLocomotive,
                time: time,
                controlUnit: "ClassificationCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);

            // Set up completion callback
            activity.OnReadyToCommence = _ =>
            {
                CommenceAndScheduleActivity(wagonGroup, trackId, activity);
            };
        }

        private void CommenceAndScheduleActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            activity.CommencedAt = _engine.Now;

            var allocatedWorkers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = new Dictionary<string, double>();
            foreach (var worker in allocatedWorkers)
            {
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);
            }

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: Commence '{activity.ActivityId}'");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: length={activity.EntityLength:F1}m base={activity.BaseSecondsPerMeter:F1}s/m avgMult={activity.AverageWorkerMultiplier:F2} -> duration={duration.TotalSeconds:F0}s");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(wagonGroup, trackId, activity),
                $"ActivityComplete-{activity.ActivityId}"
            );
        }

        private void CompleteActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: DONE '{activity.ActivityId}'");

            // Mark wagon group as secured or coupled
            if (activity is SecuringActivity)
            {
                wagonGroup.IsSecured = true;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: {wagonGroup.Id} is now SECURED");
            }
            else if (activity is CouplingActivity)
            {
                wagonGroup.IsCoupled = true;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: {wagonGroup.Id} is now COUPLED");
            }

            // Release resources
            _resourceControl.Release(activity);

            // File completion check request
            var completionCheckRequest = new CompletionCheckRequest(trackId, wagonGroup.Destination, _engine.Now);
            _completionCheckRequests.Enqueue(completionCheckRequest);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: filed CompletionCheckRequest for track {trackId}");

            // Process requests
            ProcessRequests(_engine.Now);
        }

        private void HandleCompletionCheck(CompletionCheckRequest request, DateTime time)
        {
            var trackId = request.TrackId;
            var destination = request.Destination;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: checking completion for track {trackId}");

            if (!_wagonGroupsByTrack.ContainsKey(trackId))
            {
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: no wagon groups on track {trackId}");
                return;
            }

            var wagonGroups = _wagonGroupsByTrack[trackId];
            double totalLength = wagonGroups.Sum(wg => wg.Length);
            int totalWagonCount = wagonGroups.Sum(wg => wg.WagonIds.Count);

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: track {trackId} has {wagonGroups.Count} WGs, total length={totalLength:F1}m, {totalWagonCount} wagons");

            // Check completion criteria
            if (totalLength >= MIN_TRAIN_LENGTH)
            {
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: ✓ TRAIN COMPLETE (≥{MIN_TRAIN_LENGTH}m)");

                // Create outbound train
                CreateOutboundTrain(trackId, destination, wagonGroups, time);
            }
            else
            {
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: train not yet complete ({totalLength:F1}m < {MIN_TRAIN_LENGTH}m)");
            }
        }

        private void CreateOutboundTrain(string trackId, string destination, List<WagonGroup> wagonGroups, DateTime time)
        {
            var track = _classificationTracks.First(t => t.RealLifeID == trackId);
            var train = new OutboundTrain(destination, trackId, track.Area);

            foreach (var wg in wagonGroups)
            {
                train.AddWagonGroup(wg);
            }

            _trainsByTrack[trackId] = train;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: ✨ Created {train}");

            // Request locomotive
            RequestLocomotive(train, time);
        }

        private void RequestLocomotive(OutboundTrain train, DateTime time)
        {
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: requesting locomotive for {train.Id}");

            // Create loco coupling activity
            var locoCouplingActivity = new CouplingActivity(
                wagonGroupId: $"Loco-{train.Id}",
                wagonGroupLength: 20.0, // Assume loco is 20m
                couplingToTrainId: train.Id,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time
            );

            var resourceRequest = new ResourceRequest(
                activity: locoCouplingActivity,
                workers: 2,
                loco: true, // NEED LOCOMOTIVE!
                time: time,
                controlUnit: "ClassificationCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);

            locoCouplingActivity.OnReadyToCommence = _ =>
            {
                CommenceLocoCoupling(train, locoCouplingActivity);
            };
        }

        private void CommenceLocoCoupling(OutboundTrain train, Activity activity)
        {
            activity.CommencedAt = _engine.Now;

            var allocatedWorkers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = new Dictionary<string, double>();
            foreach (var worker in allocatedWorkers)
            {
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);
            }

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: Coupling locomotive to {train.Id}");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: duration={duration.TotalSeconds:F0}s");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteLocoCoupling(train, activity),
                $"LocoCouplingComplete-{train.Id}"
            );
        }

        private void CompleteLocoCoupling(OutboundTrain train, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            // Mark train as having locomotive
            train.HasLocomotive = true;
            train.LocomotiveId = activity.AllocatedLocoIds.FirstOrDefault();

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: ✓ Locomotive {train.LocomotiveId} coupled to {train.Id}");

            // Release workers only (keep loco with train)
            foreach (var workerId in activity.AllocatedWorkerIds)
            {
                var worker = _resourceControl.GetWorkersByIds(new[] { workerId }).FirstOrDefault();
                string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {firstName} returned to pool");
                _resourceControl.ReturnWorker(workerId);
            }
            activity.AllocatedWorkerIds.Clear();

            // Request leaving preparation
            RequestLeavingPreparation(train, _engine.Now);
        }

        private void RequestLeavingPreparation(OutboundTrain train, DateTime time)
        {
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: requesting leaving preparation for {train.Id}");

            var leavingPrepActivity = new LeavingPreparationActivity(
                trainId: train.Id,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time,
                fixedDuration: TimeSpan.FromMinutes(5) // 5 min for brake test, docs, etc.
            );

            var resourceRequest = new ResourceRequest(
                activity: leavingPrepActivity,
                workers: 2,
                loco: false, // Loco already with train
                time: time,
                controlUnit: "ClassificationCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);

            leavingPrepActivity.OnReadyToCommence = _ =>
            {
                CommenceLeavingPreparation(train, leavingPrepActivity);
            };
        }

        private void CommenceLeavingPreparation(OutboundTrain train, LeavingPreparationActivity activity)
        {
            activity.CommencedAt = _engine.Now;

            var duration = activity.CalculateFixedDuration();

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: Commencing leaving preparation for {train.Id}");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: duration={duration.TotalMinutes:F1} minutes (brake test, documents, permissions)");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteLeavingPreparation(train, activity),
                $"LeavingPrepComplete-{train.Id}"
            );
        }

        private void CompleteLeavingPreparation(OutboundTrain train, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            train.LeavingPreparationComplete = true;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: ✓ Leaving preparation complete for {train.Id}");

            // Release resources
            _resourceControl.Release(activity);

            // Request departure
            var departureRequest = new TrainDepartureRequest(train, _engine.Now);
            _departureRequests.Enqueue(departureRequest);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: filed TrainDepartureRequest for {train.Id}");

            ProcessRequests(_engine.Now);
        }

        private void HandleTrainDeparture(TrainDepartureRequest request, DateTime time)
        {
            var train = request.Train;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: processing departure for {train.Id}");

            // Create departure drive activity
            var departureDrive = new DrivingActivity(
                activityType: "Departure",
                entityId: train.Id,
                entityLength: train.TotalLength,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time,
                speed: 30.0 // 30 m/min departure speed
            );

            departureDrive.OnReadyToCommence = _ =>
            {
                CommenceDepartureDrive(train, departureDrive);
            };

            // No resources needed - loco already coupled
            departureDrive.AllResourcesArrivedAt = time;
            departureDrive.OnReadyToCommence?.Invoke(departureDrive);
        }

        private void CommenceDepartureDrive(OutboundTrain train, DrivingActivity activity)
        {
            activity.CommencedAt = _engine.Now;

            double distanceToExit = 500.0; // Assume 500m to exit
            var duration = activity.CalculateDrivingDuration(distanceToExit);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: {train.Id} departing (driving {distanceToExit:F0}m to exit)");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: ETA {duration.TotalMinutes:F1} minutes");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteDepartureDrive(train, activity),
                $"DepartureComplete-{train.Id}"
            );
        }

        private void CompleteDepartureDrive(OutboundTrain train, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: 🚂 {train.Id} EXITED THE SYSTEM");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ClassificationCU: Train departed with {train.WagonGroups.Count} wagon groups, {train.TotalWagonCount} wagons, {train.TotalLength:F1}m");

            // Clean up
            _wagonGroupsByTrack.Remove(train.CurrentTrackId);
            _trainsByTrack.Remove(train.CurrentTrackId);

            // Return locomotive to pool
            if (train.LocomotiveId != null)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {train.LocomotiveId} returned to pool");
                _resourceControl.ReturnLoco(train.LocomotiveId);
            }
        }
    }
}