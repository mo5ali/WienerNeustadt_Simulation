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

        // Within-track FIFO gating for SEC / COP. At most one SEC/COP request per
        // track may be "live" (submitted to ResourceCU but not yet Started) at a
        // time. While that request is live, later arrivals on the same track are
        // held in _deferredPrepsByTrack. When the live request fires its Started
        // event, we release the next deferred WG so its SEC/COP gets submitted —
        // preserving arrival order regardless of resource availability or worker
        // travel-time variance.
        private readonly Dictionary<string, bool> _hasLiveSecCopByTrack = new();
        private readonly Dictionary<string, Queue<WagonGroupPreparationRequest>> _deferredPrepsByTrack = new();

        // Tracks for which DestinationCommittedToOutbound has already been
        // fired (early, on threshold-crossing in HandleWagonGroupArrival, or
        // late, in CreateOutboundTrain). Prevents double-firing the event
        // for the same track and lets CreateOutboundTrain skip its own emit
        // when the early signal already fired.
        private readonly HashSet<string> _committedTracks = new();

        // Number of SEC/COP activities still pending or running per track —
        // i.e. WGs that have arrived but not yet finished their per-WG prep.
        // Incremented in HandleWagonGroupArrival, decremented in
        // CompleteActivity. HandleCompletionCheck only allows OBT creation
        // when this count hits zero so an OBT can never include a WG whose
        // SEC/COP is still in flight (the bug that caused 1100402's badge
        // to flip from "Coupling" back to "Waiting" mid-OBTP).
        private readonly Dictionary<string, int> _pendingSecCopCountByTrack = new();

        // Fired the moment an OutboundTrain is created on a classification track —
        // i.e. the destination's WGs have reached the threshold and OBTP is about
        // to be requested. ArrivalCU listens to this so it can release its
        // destination→track mapping; subsequent WGs of the same destination then
        // get sorted to a fresh classification track instead of piling onto a
        // track already committed to an outbound train.
        public event Action<string, string>? DestinationCommittedToOutbound;

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
            var trackId = classificationTrack.RealLifeID;

            if (!_wagonGroupsByTrack.ContainsKey(trackId))
                _wagonGroupsByTrack[trackId] = new List<WagonGroup>();

            _wagonGroupsByTrack[trackId].Add(wagonGroup);
            _wagonGroupTracks[wagonGroup.Id] = classificationTrack;  // Track the wagon group's track

            // Every arrival schedules a SEC or COP (immediately or via the
            // deferred queue). Bump the per-track pending counter so the
            // completion check can refuse to mint an OBT until every WG on
            // the track has finished its prep.
            _pendingSecCopCountByTrack[trackId] =
                (_pendingSecCopCountByTrack.TryGetValue(trackId, out var c) ? c : 0) + 1;

            // ONLY show the newly arrived WG, not all WGs on the track
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: WG {wagonGroup.Id} arrived at track {trackId}");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, "ArrivedClassificationTrack", time, trackId);

            // EARLY commit: the moment this WG's arrival pushes the track over
            // MIN_TRAIN_LENGTH, fire DestinationCommittedToOutbound so ArrivalCU
            // releases the destination → track binding NOW. Without this, the
            // event fires only after SEC/COP completes (in CreateOutboundTrain),
            // and any PushOff that resolves its destination in that window
            // would still be routed onto this track. CreateOutboundTrain still
            // builds the actual OBT later, but it skips the duplicate emit.
            if (!_committedTracks.Contains(trackId))
            {
                double totalLengthOnTrack = _wagonGroupsByTrack[trackId].Sum(wg => wg.Length);
                if (totalLengthOnTrack >= MIN_TRAIN_LENGTH)
                {
                    var destination = wagonGroup.Destination;
                    _committedTracks.Add(trackId);
                    Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: track {trackId} crossed train-length threshold ({totalLengthOnTrack:F0}m >= {MIN_TRAIN_LENGTH:F0}m) — committing destination '{destination}' to outbound (early, before SEC/COP completes)");
                    SimulationLogger.Instance.LogTrainEvent(trackId, "DestinationCommittedEarly", time, $"{destination}|{totalLengthOnTrack:F0}m");
                    DestinationCommittedToOutbound?.Invoke(destination, trackId);
                }
            }

            var prep = new WagonGroupPreparationRequest(wagonGroup, classificationTrack, time);

            // Within-track FIFO gate: if there's already a SEC/COP submitted for
            // this track that hasn't started yet, defer this WG until that one
            // commences. Otherwise mark the track as having a live request and
            // submit immediately.
            if (_hasLiveSecCopByTrack.TryGetValue(trackId, out var hasLive) && hasLive)
            {
                if (!_deferredPrepsByTrack.ContainsKey(trackId))
                    _deferredPrepsByTrack[trackId] = new Queue<WagonGroupPreparationRequest>();
                _deferredPrepsByTrack[trackId].Enqueue(prep);
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: WG {wagonGroup.Id} deferred on track {trackId} — waiting for the earlier WG's SEC/COP to start");
                return;
            }

            _hasLiveSecCopByTrack[trackId] = true;
            _preparationRequests.Enqueue(prep);
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

            // Sets CommencedAt and emits the canonical ActivityEvent Started row.
            // (Previously CommencedAt was never being set on Coupling / Securing.)
            activity.MarkCommenced(_engine.Now);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: Commence '{activity.ActivityId}' [length={activity.EntityLength:F0}m base={activity.BaseSecondsPerMeter:F0}s/m avgMult={activity.AverageWorkerMultiplier:F0} -> duration={duration.TotalSeconds:F0}s]");
            SimulationLogger.Instance.LogWagonGroupEvent(wagonGroup.Id, activity.ActivityType + "Started", _engine.Now);

            // Within-track FIFO gate release: this SEC/COP has now Started, so
            // any WG that arrived behind it on the same track can be released
            // for its own SEC/COP submission. We release one at a time — that
            // newly-released WG will hold the gate until IT starts, etc.
            ReleaseNextDeferredPreparation(trackId, _engine.Now);

            _engine.Schedule(
                _engine.Now.Add(duration),
                () => CompleteActivity(wagonGroup, trackId, activity),
                $"Complete{activity.ActivityType}-{wagonGroup.Id}"
            );
        }

        // Pop one deferred prep request off the per-track queue (if any) and
        // submit it. Called when the previously-live SEC/COP for the track has
        // commenced. If the deferred queue is empty, the track simply has no
        // live SEC/COP — the next arrival will go through the normal path.
        private void ReleaseNextDeferredPreparation(string trackId, DateTime time)
        {
            _hasLiveSecCopByTrack[trackId] = false;

            if (!_deferredPrepsByTrack.TryGetValue(trackId, out var queue) || queue.Count == 0)
                return;

            var nextPrep = queue.Dequeue();
            Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: deferred WG {nextPrep.WagonGroup.Id} on track {trackId} now released for SEC/COP");

            _hasLiveSecCopByTrack[trackId] = true;
            _preparationRequests.Enqueue(nextPrep);
            ProcessRequests(time);
        }

        private void CompleteActivity(WagonGroup wagonGroup, string trackId, Activity activity)
        {
            activity.MarkCompleted(_engine.Now);

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

            // Pair with the increment in HandleWagonGroupArrival: this WG's
            // prep is done, so the per-track pending count drops by one.
            // Once it hits zero AND the threshold is met, HandleCompletionCheck
            // can mint the OBT.
            if (_pendingSecCopCountByTrack.TryGetValue(trackId, out var pending) && pending > 0)
                _pendingSecCopCountByTrack[trackId] = pending - 1;

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

            // Don't create an OBT while any WG on this track still has a
            // SEC/COP pending or running. Otherwise the OBT would include
            // a not-yet-prepped WG and that WG's later SEC/COP completion
            // would clobber its OBTP status badge in the visualizer (and,
            // logically, an OBT shouldn't be considered "in formation"
            // until every WG on the track is actually ready to leave).
            // The next SEC/COP completion will re-enqueue another check.
            var pending = _pendingSecCopCountByTrack.TryGetValue(trackId, out var p) ? p : 0;
            if (pending > 0)
            {
                Console.WriteLine($"{time:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: completion check for track {trackId} deferred — {pending} SEC/COP still in flight");
                return;
            }

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

            // Notify subscribers (ArrivalCU) that this destination's track is
            // now committed to an outbound train. ArrivalCU clears its
            // destination→track mapping so the NEXT WG of this destination
            // gets sorted to a different (still-free) classification track,
            // instead of piling onto this one mid-OBTP.
            //
            // Skipped if HandleWagonGroupArrival already fired the event when
            // the threshold was first crossed (the common path). Only fires
            // here for tracks that somehow reach OBT creation without ever
            // having gone through the threshold-cross signal — defensive.
            if (_committedTracks.Add(trackId))
            {
                DestinationCommittedToOutbound?.Invoke(destination, trackId);
            }

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

            activity.MarkCommenced(_engine.Now);

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
            activity.MarkCompleted(_engine.Now);

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
                time,
                trainLength: train.TotalLength
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

            activity.MarkCommenced(_engine.Now);

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
            activity.MarkCompleted(_engine.Now);
            SimulationLogger.Instance.LogTrainEvent(train.Id, "Departed", _engine.Now, $"{train.TotalLength:F1}m to {train.Destination}");

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ClassifCU: DONE '{activity.ActivityId}', '{train.Id}' removed from system");

            _wagonGroupsByTrack.Remove(train.CurrentTrackId);
            _trainsByTrack.Remove(train.CurrentTrackId);

            // Track is free again; clear the early-commit marker so a future
            // cycle on this same physical track can re-fire DestinationCommittedToOutbound.
            _committedTracks.Remove(train.CurrentTrackId);

            // Also clear the per-track pending-SEC/COP counter so the next
            // cycle on this physical track starts from a clean slate.
            _pendingSecCopCountByTrack.Remove(train.CurrentTrackId);

            var track = _classificationTracks.FirstOrDefault(t => t.RealLifeID == train.CurrentTrackId);
            if (track != null)
            {
                track.CurrentOccupancies.Clear();
                // Unreserve the track now that the outbound train has departed.
                // Without this, every track ever used by an OBT stays Reserved
                // forever, and ArrivalCU eventually runs out of unreserved
                // tracks for new destination assignments — especially after
                // DestinationCommittedToOutbound starts cycling tracks.
                track.Reserved = false;
            }

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