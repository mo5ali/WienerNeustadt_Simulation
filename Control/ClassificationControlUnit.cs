using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Models;
using WienerNeustadtSimulation.Output;
using WienerNeustadtSimulation.Infrastructure;

namespace WienerNeustadtSimulation.Control
{
    public class ClassificationControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ResourceControlUnit _resourceControl;
        private readonly List<Track> _classificationTracks;
        private readonly Dictionary<string, WagonGroupDto> _wagonGroupData;

        // Track which track each WagonGroup is on
        private readonly Dictionary<string, Track> _wagonGroupTracks = new();
        private readonly Dictionary<string, List<WagonGroup>> _wagonGroupsByTrack = new();
        private readonly Dictionary<string, OutboundTrain> _trainsByTrack = new();

        private readonly Queue<WagonGroupPreparationRequest> _preparationRequests = new();
        private readonly Queue<CompletionCheckRequest> _completionCheckRequests = new();
        private readonly Queue<TrainDepartureRequest> _departureRequests = new();

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
        }

        // ─── Request queue structures ──────────────────────────────────────────

        public class WagonGroupPreparationRequest
        {
            public WagonGroup WagonGroup { get; set; }
            public Track Track { get; set; }  // ADD: Track reference
            public DateTime RequestedAt { get; set; }

            public WagonGroupPreparationRequest(WagonGroup wagonGroup, Track track, DateTime requestedAt)
            {
                WagonGroup = wagonGroup;
                Track = track;  // ADD
                RequestedAt = requestedAt;
            }
        }

        public class CompletionCheckRequest
        {
            public string TrackId { get; set; }
            public DateTime RequestedAt { get; set; }

            public CompletionCheckRequest(string trackId, DateTime requestedAt)
            {
                TrackId = trackId;
                RequestedAt = requestedAt;
            }
        }

        public class TrainDepartureRequest
        {
            public OutboundTrain Train { get; set; }
            public DateTime RequestedAt { get; set; }

            public TrainDepartureRequest(OutboundTrain train, DateTime requestedAt)
            {
                Train = train;
                RequestedAt = requestedAt;
            }
        }

        // ─── Wagon Group Arrival ────────────────────────────────────────────────

        public void HandleWagonGroupArrival(WagonGroup wagonGroup, Track classificationTrack, DateTime time)
        {
            if (!_wagonGroupsByTrack.ContainsKey(classificationTrack.RealLifeID))
                _wagonGroupsByTrack[classificationTrack.RealLifeID] = new List<WagonGroup>();

            _wagonGroupsByTrack[classificationTrack.RealLifeID].Add(wagonGroup);
            _wagonGroupTracks[wagonGroup.Id] = classificationTrack;  // Track the wagon group's track

            // ONLY show the newly arrived WG, not all WGs on the track
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: WG {wagonGroup.Id} arrived at track {classificationTrack.RealLifeID}");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "ArrivedClassificationTrack", time, classificationTrack.RealLifeID);

            _preparationRequests.Enqueue(new WagonGroupPreparationRequest(wagonGroup, classificationTrack, time));
            ProcessRequests(time);
        }

        // ─── Wagon Group Preparation ────────────────��──────────────────────────

        private void HandleWagonGroupPreparation(WagonGroupPreparationRequest request, DateTime time)
        {
            var wagonGroup = request.WagonGroup;
            var track = request.Track;
            var trackId = track.RealLifeID;

            Activity activity;

            bool isTrackEmpty = !_wagonGroupsByTrack.ContainsKey(trackId) || _wagonGroupsByTrack[trackId].Count == 1;

            if (isTrackEmpty)
            {
                var activityIdPreview = $"Act_SEC_{wagonGroup.Id}_{time:HHmmss}_{trackId}";
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: track {trackId} is EMPTY → initialize {activityIdPreview}");
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "SecuringRequested", time);

                activity = new SecuringActivity(
                    wagonGroup.Id,
                    wagonGroup.Length,
                    trackId,
                    "Classification",
                    "ClassificationCU",
                    time
                );
            }
            else
            {
                var existingTrain = _trainsByTrack.ContainsKey(trackId) ? _trainsByTrack[trackId] : null;
                string couplingToId = existingTrain?.Id ?? "existing-wgs";

                var activityIdPreview = $"Act_COP_{wagonGroup.Id}_{time:HHmmss}_{trackId}";
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: track {trackId} has WGs → initialize {activityIdPreview}");
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "CouplingRequested", time);

                activity = new CouplingActivity(
                    wagonGroup.Id,
                    wagonGroup.Length,
                    "existing-wgs",  // couplingToTrainId parameter
                    trackId,
                    "Classification",
                    "ClassificationCU",
                    time,
                    locoStaysWithEntity: false
                );
            }

            activity.OnReadyToCommence = (act) => CommenceAndScheduleActivity(wagonGroup, trackId, act);
            activity.OnCompleted = (act) => CompleteActivity(wagonGroup, trackId, act);

            _resourceControl.SubmitRequest(new ResourceRequest(
                activity,
                activity.RequiredWorkers,
                activity.RequiresLocomotive,
                time,
                $"Req_{activity.ActivityId}"
            ));
        }

        private void CommenceAndScheduleActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            var workers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = workers
                .Where(w => w.Id != null)
                .ToDictionary(
                    w => w.Id!,
                    w => _resourceControl.GetWorkerTimeMultiplierForActivity(w, activity.ActivityType)
                );

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commence '{activity.ActivityId}' [length={activity.EntityLength:F0}m base={activity.BaseSecondsPerMeter:F0}s/m avgMult={activity.AverageWorkerMultiplier:F0} -> duration={duration.TotalSeconds:F0}s]");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, activity.ActivityType + "Started", _engine.Now);

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(wagonGroup, trackId, activity),
                $"Complete{activity.ActivityType}-{wagonGroup.Id}"
            );
        }

        private void CompleteActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            activity.CompletedAt = _engine.Now;

            if (activity is SecuringActivity)
            {
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "Secured", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}' - {wagonGroup.Id} is SECURED");
            }
            else if (activity is CouplingActivity)
            {
                SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "Coupled", _engine.Now);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}' - {wagonGroup.Id} is COUPLED");
            }

            _resourceControl.Release(activity);

            _completionCheckRequests.Enqueue(new CompletionCheckRequest(trackId, _engine.Now));
            ProcessRequests(_engine.Now);
        }

        // ─── Train Completion Check ────────────────────────────────────────────

        private void HandleCompletionCheck(CompletionCheckRequest request, DateTime time)
        {
            var trackId = request.TrackId;

            if (!_wagonGroupsByTrack.ContainsKey(trackId))
                return;

            var wagonGroups = _wagonGroupsByTrack[trackId];
            var destination = wagonGroups.FirstOrDefault()?.Destination;

            if (string.IsNullOrEmpty(destination))
                return;

            double totalLength = wagonGroups.Sum(wg => wg.Length);

            if (totalLength >= MIN_TRAIN_LENGTH)
            {
                CreateOutboundTrain(trackId, destination, wagonGroups, time);
            }
        }

        // ─── Outbound Train Creation ───────────────────────────────────────────

        private void CreateOutboundTrain(string trackId, string destination, List<WagonGroup> wagonGroups, DateTime time)
        {
            var train = new OutboundTrain(trackId, destination, wagonGroups, time);

            _trainsByTrack[trackId] = train;

            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: TRAIN COMPLETE '{train.Id}' on track {trackId} ({train.TotalLength:F0}m, {train.WagonGroups.Count} WGs)");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "OutboundTrainCreated", time, destination);

            // CRITICAL FIX: Remove wagon groups from tracking - they're now part of a train
            // and should NOT be counted again in future completion checks
            _wagonGroupsByTrack.Remove(trackId);  // ← ADD THIS LINE

            RequestOutboundTrainPreparation(train, time);
        }


        // ─── Outbound Train Preparation (OBTP) ─────────────────────────────────

        private void RequestOutboundTrainPreparation(OutboundTrain train, DateTime time)
        {
            var activityIdPreview = $"Act_OBTP_{train.Id}_{time:HHmmss}_{train.CurrentTrackId}";
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: initialized '{activityIdPreview}'");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "OBTPRequested", time);

            var obtpActivity = new OutboundTrainPreparationActivity(
                _engine,
                train.Id,
                train.TotalLength,
                train.CurrentTrackId,
                "Classification",
                "ClassificationCU",
                time
            );

            obtpActivity.OnReadyToCommence = (act) => CommenceOutboundTrainPreparation(train, (OutboundTrainPreparationActivity)act);
            obtpActivity.OnCompleted = (act) => CompleteOutboundTrainPreparation(train, (OutboundTrainPreparationActivity)act);

            _resourceControl.SubmitRequest(new ResourceRequest(
                obtpActivity,
                2,  // 2 workers
                false,  // no shunting loco
                time,
                $"Req_{obtpActivity.ActivityId}"
            ));
        }

        private void CommenceOutboundTrainPreparation(OutboundTrain train, OutboundTrainPreparationActivity activity)
        {
            var workers = _resourceControl.GetWorkersByIds(activity.AllocatedWorkerIds);
            var workerMultipliers = workers
                .Where(w => w.Id != null)
                .ToDictionary(
                    w => w.Id!,
                    w => _resourceControl.GetWorkerTimeMultiplierForActivity(w, "OutboundTrainPreparation")
                );

            var duration = activity.CalculateDuration(workerMultipliers);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commence '{activity.ActivityId}' [length={activity.EntityLength:F1}m base={activity.BaseSecondsPerMeter:F1}s/m avgMult={activity.AverageWorkerMultiplier:F2} -> duration={duration.TotalSeconds:F0}s]");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "OBTPStarted", _engine.Now);

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteOutboundTrainPreparation(train, activity),
                $"CompleteOBTP-{train.Id}"
            );
        }

        private void CompleteOutboundTrainPreparation(OutboundTrain train, OutboundTrainPreparationActivity activity)
        {
            activity.CompletedAt = _engine.Now;

            train.LocomotiveId = activity.AllocatedLocoIds.FirstOrDefault();
            SimulationLogger.Instance.LogTrainEvent(train.Id, "OBTPComplete", _engine.Now, train.LocomotiveId ?? "");

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}'");

            _resourceControl.Release(activity);

            RequestDepartureDrive(train, _engine.Now);
        }

        // ─── Departure Drive (DEPD) ────────────────────────────────────────────

        private void RequestDepartureDrive(OutboundTrain train, DateTime time)
        {
            var activityIdPreview = $"Act_DEPD_{train.Id}_{time:HHmmss}_{train.CurrentTrackId}";
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: initialized '{activityIdPreview}'");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "DEPDRequested", time);

            var depdActivity = new DepartureDriveActivity(
                _engine,
                train.Id,
                train.CurrentTrackId,
                "Exit",
                "ClassificationCU",
                time
            );

            depdActivity.OnReadyToCommence = (act) => CommenceDepartureDrive(train, (DepartureDriveActivity)act);
            depdActivity.OnCompleted = (act) => CompleteDepartureDrive(train, (DepartureDriveActivity)act);

            _resourceControl.SubmitRequest(new ResourceRequest(
                depdActivity,
                0,  // no workers
                false,  // no loco
                time,
                $"Req_{depdActivity.ActivityId}"
            ));
        }

        private void CommenceDepartureDrive(OutboundTrain train, DepartureDriveActivity activity)
        {
            var duration = activity.CalculateFixedDuration();

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commence '{activity.ActivityId}' [duration={duration.TotalSeconds:F0}s]");
            SimulationLogger.Instance.LogTrainEvent(train.Id, "DEPDStarted", _engine.Now);

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteDepartureDrive(train, activity),
                $"CompleteDEPD-{train.Id}"
            );
        }

        private void CompleteDepartureDrive(OutboundTrain train, DepartureDriveActivity activity)
        {
            activity.CompletedAt = _engine.Now;
            SimulationLogger.Instance.LogTrainEvent(train.Id, "Departed", _engine.Now, $"{train.TotalLength:F1}m to {train.Destination}");

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}', '{train.Id}' removed from system");

            _wagonGroupsByTrack.Remove(train.CurrentTrackId);
            _trainsByTrack.Remove(train.CurrentTrackId);

            var track = _classificationTracks.FirstOrDefault(t => t.RealLifeID == train.CurrentTrackId);
            track?.CurrentOccupancies.Clear();

            _resourceControl.Release(activity);

            if (train.LocomotiveId != null)
            {
                _resourceControl.ReturnLoco(train.LocomotiveId);
            }
        }

        // ─── Request Queue Processing ──────────────────────────────────────────

        private void ProcessRequests(DateTime time)
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
        }
    }
}