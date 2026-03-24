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

        // ─── Entry point from PushOffActivity ─────────────────────────────────

        public void HandleWagonGroupArrival(WagonGroup wagonGroup, Track classificationTrack, DateTime time)
        {
            if (!_wagonGroupsByTrack.ContainsKey(classificationTrack.RealLifeID))
                _wagonGroupsByTrack[classificationTrack.RealLifeID] = new List<WagonGroup>();

            _wagonGroupsByTrack[classificationTrack.RealLifeID].Add(wagonGroup);

            var wgIdsList = string.Join(", ", _wagonGroupsByTrack[classificationTrack.RealLifeID].Select(wg => wg.Id));
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: WGs [{wgIdsList}] arrived at track {classificationTrack.RealLifeID} ~ entity(s) created");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "ArrivedClassificationTrack", time, classificationTrack.RealLifeID);

            _preparationRequests.Enqueue(new WagonGroupPreparationRequest(wagonGroup, time));
            ProcessRequests(time);
        }

        // ─── Wagon group preparation (Secure or Couple) ────────────────────────

        private void HandleWagonGroupPreparation(WagonGroupPreparationRequest request, DateTime time)
        {
            var wagonGroup = request.WagonGroup;
            var trackId = wagonGroup.CurrentTrackId;

            bool isTrackEmpty = _wagonGroupsByTrack[trackId].Count == 1;

            Activity activity;

            if (isTrackEmpty)
            {
                var activityIdPreview = $"Act_SEC_{time:yyMMddHHmmss}_ClassifCU_{wagonGroup.Id}";
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: track {trackId} is EMPTY → initialize {activityIdPreview}");
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "SecuringRequested", time);

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
                var existingTrain = _trainsByTrack.ContainsKey(trackId) ? _trainsByTrack[trackId] : null;
                string couplingToId = existingTrain?.Id ?? "existing-wgs";

                var activityIdPreview = $"Act_COP_{time:yyMMddHHmmss}_ClassifCU_{wagonGroup.Id}";
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: track {trackId} has WGs → initialize {activityIdPreview}");
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "CouplingRequested", time);

                // WG-to-WG coupling: loco not involved, does not stay.
                activity = new CouplingActivity(
                    wagonGroupId: wagonGroup.Id,
                    wagonGroupLength: wagonGroup.Length,
                    couplingToTrainId: couplingToId,
                    location: trackId,
                    area: wagonGroup.CurrentArea,
                    controlUnit: "ClassificationCU",
                    requestedAt: time,
                    locoStaysWithEntity: false
                );
            }

            var resourceRequest = new ResourceRequest(
                activity: activity,
                workers: activity.RequiredWorkers,
                loco: activity.RequiresLocomotive,
                time: time,
                controlUnit: "ClassificationCU"
            );

            _resourceControl.SubmitRequest(resourceRequest);

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
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commence '{activity.ActivityId}' [{activity.EntityLength:F0}m base={activity.BaseSecondsPerMeter:F0}s/m avgMult={activity.AverageWorkerMultiplier:F0} > dur={duration.TotalSeconds:F0}s]");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, activity.ActivityType + "Started", _engine.Now);

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(wagonGroup, trackId, activity),
                $"ActivityComplete-{activity.ActivityId}"
            );
        }

        private void CompleteActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            if (activity is SecuringActivity)
            {
                wagonGroup.IsSecured = true;
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "Secured", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}' - {wagonGroup.Id} is SECURED");
            }
            else if (activity is CouplingActivity)
            {
                wagonGroup.IsCoupled = true;
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "Coupled", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}' - {wagonGroup.Id} is COUPLED");
            }

            // Uniform release: SecuringActivity and CouplingActivity (WG-to-WG) both
            // declare LocoStaysWithEntity=false, WorkersReleasedIndividually=true.
            _resourceControl.Release(activity);

            var completionCheckRequest = new CompletionCheckRequest(trackId, wagonGroup.Destination, _engine.Now);
            _completionCheckRequests.Enqueue(completionCheckRequest);

            ProcessRequests(_engine.Now);
        }

        // ─── Completion check ──────────────────────────────────────────────────

        private void HandleCompletionCheck(CompletionCheckRequest request, DateTime time)
        {
            var trackId = request.TrackId;
            var destination = request.Destination;

            if (!_wagonGroupsByTrack.ContainsKey(trackId))
                return;

            var wagonGroups = _wagonGroupsByTrack[trackId];
            double totalLength = wagonGroups.Sum(wg => wg.Length);

            if (totalLength >= MIN_TRAIN_LENGTH)
            {
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: TRAIN COMPLETE on track {trackId} ({totalLength:F1}m >= {MIN_TRAIN_LENGTH}m, {wagonGroups.Count} WGs)");
                SimulationLogger.Instance.LogTrainEvent($"Train-{trackId}", "TrainComplete", time, $"{totalLength:F1}m");

                CreateOutboundTrain(trackId, destination, wagonGroups, time);
            }
        }

        // ─── Outbound train creation ───────────────────────────────────────────

        private void CreateOutboundTrain(string trackId, string destination, List<WagonGroup> wagonGroups, DateTime time)
        {
            var track = _classificationTracks.First(t => t.RealLifeID == trackId);
            var train = new OutboundTrain(destination, trackId, track.Area);

            foreach (var wg in wagonGroups)
                train.AddWagonGroup(wg);

            _trainsByTrack[trackId] = train;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Created {train}");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "OutboundTrainCreated", time, destination);

            RequestLocomotive(train, time);
        }

        // ─── Locomotive coupling ───────────────────────────────────────────────

        private void RequestLocomotive(OutboundTrain train, DateTime time)
        {
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: requesting locomotive for {train.Id}");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LocomotiveRequested", time);

            // locoStaysWithEntity: true — the loco stays coupled to the outbound train
            // until it exits the system. Release() will NOT return it to the pool.
            // ReturnLoco() is called explicitly in CompleteDepartureDrive().
            var locoCouplingActivity = new CouplingActivity(
                wagonGroupId: $"Loco-{train.Id}",
                wagonGroupLength: 20.0,
                couplingToTrainId: train.Id,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time,
                locoStaysWithEntity: true
            );

            var resourceRequest = new ResourceRequest(
                activity: locoCouplingActivity,
                workers: 2,
                loco: true,
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
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LocoCouplingStarted", _engine.Now);

            var allocatedWorkers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = new Dictionary<string, double>();
            foreach (var worker in allocatedWorkers)
                workerMultipliers[worker.Id] = _resourceControl.GetWorkerTimeMultiplierForActivity(worker, activity.ActivityType);

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Coupling locomotive to {train.Id}");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: duration={duration.TotalSeconds:F0}s");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteLocoCoupling(train, activity),
                $"LocoCouplingComplete-{train.Id}"
            );
        }

        private void CompleteLocoCoupling(OutboundTrain train, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            // Read the loco ID before Release() (it stays in AllocatedLocoIds because
            // LocoStaysWithEntity=true, but we record it on the train entity now).
            train.HasLocomotive = true;
            train.LocomotiveId = activity.AllocatedLocoIds.FirstOrDefault();
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LocomotiveCoupled", _engine.Now, train.LocomotiveId);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: ✓ Locomotive {train.LocomotiveId} coupled to {train.Id}");

            // Uniform release:
            //   CouplingActivity (locoStaysWithEntity=true)  → loco NOT returned to pool ✓
            //   CouplingActivity (WorkersReleasedIndividually=true) → each worker dispatched alone ✓
            _resourceControl.Release(activity);

            RequestLeavingPreparation(train, _engine.Now);
        }

        // ─── Leaving preparation ───────────────────────────────────────────────

        private void RequestLeavingPreparation(OutboundTrain train, DateTime time)
        {
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: requesting leaving preparation for {train.Id}");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LeavingPrepRequested", time);

            var leavingPrepActivity = new LeavingPreparationActivity(
                trainId: train.Id,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time,
                fixedDuration: TimeSpan.FromMinutes(5)
            );

            var resourceRequest = new ResourceRequest(
                activity: leavingPrepActivity,
                workers: 2,
                loco: false,
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
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LeavingPrepStarted", _engine.Now);

            var duration = activity.CalculateFixedDuration();

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commencing leaving preparation for {train.Id}");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: duration={duration.TotalMinutes:F1} minutes (brake test, documents, permissions)");

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
            SimulationLogger.Instance.LogTrainEvent(train.Id, "LeavingPrepComplete", _engine.Now);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: ✓ Leaving preparation complete for {train.Id}");

            // Uniform release:
            //   LeavingPreparationActivity: LocoStaysWithEntity=false (no loco) ✓
            //                               WorkersReleasedIndividually=true     ✓
            _resourceControl.Release(activity);

            var departureRequest = new TrainDepartureRequest(train, _engine.Now);
            _departureRequests.Enqueue(departureRequest);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: filed TrainDepartureRequest for {train.Id}");

            ProcessRequests(_engine.Now);
        }

        // ─── Departure ─────────────────────────────────────────────────────────

        private void HandleTrainDeparture(TrainDepartureRequest request, DateTime time)
        {
            var train = request.Train;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: processing departure for {train.Id}");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "DepartureProcessing", time);

            var departureDrive = new DrivingActivity(
                activityType: "Departure",
                entityId: train.Id,
                entityLength: train.TotalLength,
                location: train.CurrentTrackId,
                area: train.CurrentArea,
                controlUnit: "ClassificationCU",
                requestedAt: time,
                speed: 30.0
            );

            departureDrive.OnReadyToCommence = _ =>
            {
                CommenceDepartureDrive(train, departureDrive);
            };

            // No resources needed — loco already coupled to the train.
            departureDrive.AllResourcesArrivedAt = time;
            departureDrive.OnReadyToCommence?.Invoke(departureDrive);
        }

        private void CommenceDepartureDrive(OutboundTrain train, DrivingActivity activity)
        {
            activity.CommencedAt = _engine.Now;
            SimulationLogger.Instance.LogTrainEvent(train.Id, "DepartureStarted", _engine.Now);

            double distanceToExit = 500.0;
            var duration = activity.CalculateDrivingDuration(distanceToExit);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: {train.Id} departing (driving {distanceToExit:F0}m to exit)");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: ETA {duration.TotalMinutes:F1} minutes");

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteDepartureDrive(train, activity),
                $"DepartureComplete-{train.Id}"
            );
        }

        private void CompleteDepartureDrive(OutboundTrain train, Activity activity)
        {
            activity.CompletedAt = _engine.Now;
            SimulationLogger.Instance.LogTrainEvent(train.Id, "Departed", _engine.Now, $"{train.TotalLength:F1}m to {train.Destination}");

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: 🚂 {train.Id} EXITED THE SYSTEM");
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Train departed with {train.WagonGroups.Count} wagon groups, {train.TotalWagonCount} wagons, {train.TotalLength:F1}m");

            _wagonGroupsByTrack.Remove(train.CurrentTrackId);
            _trainsByTrack.Remove(train.CurrentTrackId);

            // The loco physically left with the train. DrivingActivity declares
            // LocoStaysWithEntity=true, so Release() did not return it to the pool.
            // We return it explicitly here now that the train has exited the system.
            if (train.LocomotiveId != null)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {train.LocomotiveId} returned to pool");
                _resourceControl.ReturnLoco(train.LocomotiveId);
            }
        }

        // ─── Request processing ────────────────────────────────────────────────

        public void ProcessRequests(DateTime time)
        {
            while (_preparationRequests.Count > 0)
            {
                var request = _preparationRequests.Dequeue();
                HandleWagonGroupPreparation(request, time);
            }

            while (_completionCheckRequests.Count > 0)
            {
                var request = _completionCheckRequests.Dequeue();
                HandleCompletionCheck(request, time);
            }

            while (_departureRequests.Count > 0)
            {
                var request = _departureRequests.Dequeue();
                HandleTrainDeparture(request, time);
            }
        }
    }
}