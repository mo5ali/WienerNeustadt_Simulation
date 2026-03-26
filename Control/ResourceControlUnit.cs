using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Models;
using WienerNeustadtSimulation.Output;

namespace WienerNeustadtSimulation.Control
{
    public class ResourceControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly Queue<ResourceRequest> _requestQueue;

        private readonly List<WorkerDto> _workers;
        private readonly List<ShuntingLocomotiveDto> _shuntingLocomotives;

        private readonly HashSet<string> _availableWorkerIds;
        private readonly HashSet<string> _availableLocoIds;
        private readonly HashSet<string> _availableTrainLocoIds = new();  // NEW: Train locomotives (infinite)
        private bool _exitGateAvailable = true;  // NEW: Exit gate (one resource)

        private const double FixedTravelDistanceMeters = 100.0;
        private const double DefaultWorkerSpeedMetersPerMinute = 80.0;
        private const double LocoSpeedMetersPerMinute = 25.0;

        public ResourceControlUnit(SimulationEngine engine, ResourcePoolRoot resourcePool)
        {
            _engine = engine;
            _requestQueue = new Queue<ResourceRequest>();

            _workers = resourcePool.Workers ?? new List<WorkerDto>();
            _shuntingLocomotives = resourcePool.ShuntingLocomotives ?? new List<ShuntingLocomotiveDto>();

            _availableWorkerIds = new HashSet<string>(_workers.Select(w => w.Id ?? "").Where(id => !string.IsNullOrWhiteSpace(id)));
            _availableLocoIds = new HashSet<string>(_shuntingLocomotives.Select(l => l.Id ?? "").Where(id => !string.IsNullOrWhiteSpace(id)));

            // Initialize infinite train locomotives (we'll create IDs on demand)
            for (int i = 1; i <= 100; i++)
            {
                _availableTrainLocoIds.Add($"TL{i:D3}");
            }

            Console.WriteLine($"  → ResourceControlUnit: {_workers.Count} workers, {_shuntingLocomotives.Count} shunting locomotives, infinite train locomotives initialized");
        }

        // ─── Request submission ────────────────────────────────────────────────

        public void SubmitRequest(ResourceRequest request)
        {
            string resourceDesc = $"{request.RequiredWorkers} worker{(request.RequiredWorkers != 1 ? "s" : "")}";
            if (request.RequiresLocomotive)
                resourceDesc += " + 1 loco";

            // NEW: Check for train locomotive
            if (request.Activity is OutboundTrainPreparationActivity)
                resourceDesc = $"{request.RequiredWorkers} workers + 1 train locomotive";

            // NEW: Check for exit gate
            if (request.Activity is DepartureDriveActivity)
                resourceDesc = "exit gate";

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: request 'Req_{request.Activity.ActivityId}' received ({resourceDesc})");
            SimulationLogger.Instance.LogActivityEvent(request.RequestId, "ResourceRequest", _engine.Now, "Submitted", resourceDesc);

            if (TryAllocateAndDispatchResources(request))
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: request '{request.RequestId}' QUEUED (insufficient resources)");
            _requestQueue.Enqueue(request);
        }

        // ─── Allocation ────────────────────────────────────────────────────────

        private bool TryAllocateAndDispatchResources(ResourceRequest request)
        {
            Activity activity = request.Activity;

            // Handle exit gate for departure
            if (activity is DepartureDriveActivity)
            {
                if (!_exitGateAvailable)
                    return false;

                _exitGateAvailable = false;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: Exit cleared for 'Req_{activity.ActivityId}'");

                // No travel time for exit gate, directly trigger commencement
                activity.OnReadyToCommence?.Invoke(activity);
                return true;
            }

            // Handle train locomotive for OBTP
            bool needsTrainLoco = activity is OutboundTrainPreparationActivity;

            bool needsWorkers = request.RequiredWorkers > 0;
            bool needsLoco = request.RequiresLocomotive;

            if (needsWorkers && _availableWorkerIds.Count < request.RequiredWorkers)
                return false;

            if (needsLoco && _availableLocoIds.Count == 0)
                return false;

            if (needsTrainLoco && _availableTrainLocoIds.Count == 0)
                return false;

            // Allocate workers
            List<string> allocatedWorkers = new List<string>();
            if (needsWorkers)
            {
                allocatedWorkers = _availableWorkerIds.Take(request.RequiredWorkers).ToList();
                foreach (var id in allocatedWorkers)
                    _availableWorkerIds.Remove(id);
                activity.AllocatedWorkerIds.AddRange(allocatedWorkers);
            }

            // Allocate shunting locomotive
            List<string> allocatedLocos = new List<string>();
            if (needsLoco)
            {
                var locoId = _availableLocoIds.First();
                _availableLocoIds.Remove(locoId);
                activity.AllocatedLocoIds.Add(locoId);
                allocatedLocos.Add(locoId);
            }

            // Allocate train locomotive
            List<string> allocatedTrainLocos = new List<string>();
            if (needsTrainLoco)
            {
                var trainLocoId = _availableTrainLocoIds.First();
                _availableTrainLocoIds.Remove(trainLocoId);
                activity.AllocatedLocoIds.Add(trainLocoId);
                allocatedTrainLocos.Add(trainLocoId);
            }

            // Build compact allocation message (workers + train loco)
            List<string> workerDetails = new List<string>();
            foreach (var wId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == wId);
                string firstName = worker?.Name?.Split(' ')[0] ?? wId;
                var travelTime = CalculateWorkerTravelTime(wId);
                workerDetails.Add($"{firstName}({travelTime.TotalSeconds:F0}s/100m)");
                SimulationLogger.Instance.LogWorkerEvent(wId, "Allocated", _engine.Now, activity.ActivityId);
            }

            List<string> locoDetails = new List<string>();
            foreach (var lId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();
                locoDetails.Add($"{lId}({travelTime.TotalSeconds:F0}s/100m)");
                SimulationLogger.Instance.LogWorkerEvent(lId, "Allocated", _engine.Now, activity.ActivityId);
            }

            // Train locomotives
            foreach (var tlId in allocatedTrainLocos)
            {
                var travelTime = CalculateTrainLocoTravelTime();
                locoDetails.Add($"{tlId}({travelTime.TotalSeconds:F0}s/100m)");
                SimulationLogger.Instance.LogWorkerEvent(tlId, "Allocated", _engine.Now, activity.ActivityId);
            }

            var allResources = workerDetails.Concat(locoDetails).ToList();
            if (allResources.Count > 0)
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {string.Join(" ", allResources)} traveling to track {activity.Location} for '{activity.ActivityId}'");

            // Schedule travel for workers
            foreach (var workerId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == workerId);
                string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
                var travelTime = CalculateWorkerTravelTime(workerId);

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnWorkerArrived(activity, workerId, firstName),
                    $"WorkerArrives-{workerId}-{activity.ActivityId}"
                );
            }

            // Schedule travel for shunting locomotive
            foreach (var locoId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnLocoArrived(activity, locoId),
                    $"LocoArrives-{locoId}-{activity.ActivityId}"
                );
            }

            // Schedule travel for train locomotive
            foreach (var trainLocoId in allocatedTrainLocos)
            {
                var travelTime = CalculateTrainLocoTravelTime();

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnTrainLocoArrived(activity, trainLocoId),
                    $"TrainLocoArrives-{trainLocoId}-{activity.ActivityId}"
                );
            }

            return true;
        }

        // ─── Arrival callbacks ─────────────────────────────────────────────────
        private void OnTrainLocoArrived(Activity activity, string trainLocoId)
        {
            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count + 1;
            int total = activity.RequiredWorkers + 1;  // 2 workers + 1 train loco

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: Train locomotive arrived for '{activity.ActivityId}' ({arrived}/{total})");
            SimulationLogger.Instance.LogWorkerEvent(trainLocoId, "Arrived", _engine.Now, activity.ActivityId);

            activity.RecordLocoArrival(trainLocoId, _engine.Now);
        }

        private TimeSpan CalculateTrainLocoTravelTime()
        {
            double speed = 80.0;  // Same as fastest workers
            return TimeSpan.FromMinutes(FixedTravelDistanceMeters / speed);
        }

        private void OnWorkerArrived(Activity activity, string workerId, string workerName)
        {
            // Pre-calculate the count BEFORE recording, then add 1 for this arrival.
            // This way the log line prints BEFORE RecordWorkerArrival fires
            // CheckAndTriggerCommencement → OnReadyToCommence, ensuring the arrival
            // message always appears before the "Commence" message in the log.
            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count + 1;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {workerName} arrived for '{activity.ActivityId}' ({arrived}/{total})");
            SimulationLogger.Instance.LogWorkerEvent(workerId, "Arrived", _engine.Now, activity.ActivityId);

            // Record AFTER logging — this may immediately fire OnReadyToCommence
            // if this is the last required resource, and that callback will print
            // the "Commence" line. Logging first guarantees correct print order.
            activity.RecordWorkerArrival(workerId, _engine.Now);
        }

        private void OnLocoArrived(Activity activity, string locoId)
        {
            // Same pattern: log first, record after.
            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count + 1;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {locoId} arrived for '{activity.ActivityId}' ({arrived}/{total})");
            SimulationLogger.Instance.LogWorkerEvent(locoId, "Arrived", _engine.Now, activity.ActivityId);

            // Record AFTER logging — this may immediately fire OnReadyToCommence
            // if the loco was the last required resource.
            activity.RecordLocoArrival(locoId, _engine.Now);
        }

        // ─── Uniform release ────────────────────────────────────────────────────

        /// <summary>
        /// The single, uniform release path. Every control unit calls this on activity
        /// completion. The activity's own declared policies (LocoStaysWithEntity,
        /// WorkersReleasedIndividually) drive all resource return behaviour.
        /// </summary>
        public void Release(Activity activity)
        {
            // Exit gate release
            if (activity is DepartureDriveActivity)
            {
                _exitGateAvailable = true;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: Exit gate released");
                ProcessQueue();
                return;
            }

            // Workers
            var workerIds = activity.AllocatedWorkerIds.ToList();
            activity.AllocatedWorkerIds.Clear();

            if (workerIds.Count > 0)
            {
                ReturnWorkersBatch(workerIds);
            }

            // Locomotives (shunting or train)
            if (!activity.LocoStaysWithEntity)
            {
                var locoIds = activity.AllocatedLocoIds.ToList();
                activity.AllocatedLocoIds.Clear();

                if (locoIds.Count > 0)
                {
                    // Check if train locomotives (start with "TL")
                    var trainLocos = locoIds.Where(id => id.StartsWith("TL")).ToList();
                    var shuntingLocos = locoIds.Except(trainLocos).ToList();

                    if (trainLocos.Any())
                        ReturnTrainLocosBatch(trainLocos);

                    if (shuntingLocos.Any())
                        ReturnLocosBatch(shuntingLocos);
                }
            }
        }

        private void ReturnTrainLocosBatch(List<string> trainLocoIds)
        {
            foreach (var tlId in trainLocoIds)
            {
                _availableTrainLocoIds.Add(tlId);
                SimulationLogger.Instance.LogWorkerEvent(tlId, "Returned", _engine.Now);
            }

            var locoList = string.Join(", ", trainLocoIds);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {locoList} returned to train waiting area");

            ProcessQueue();
        }

        // ─── Low-level pool helpers ────────────────────────────────────────────

        private void ReturnWorkerInternal(string workerId)
        {
            _availableWorkerIds.Add(workerId);

            var worker = _workers.FirstOrDefault(w => w.Id == workerId);
            string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {firstName} available, returning to waiting area");
            SimulationLogger.Instance.LogWorkerEvent(workerId, "Returned", _engine.Now);

            ProcessQueue();
        }

        private void ReturnWorkersBatch(List<string> workerIds)
        {
            foreach (var workerId in workerIds)
            {
                _availableWorkerIds.Add(workerId);
                SimulationLogger.Instance.LogWorkerEvent(workerId, "Returned", _engine.Now);
            }

            var names = workerIds.Select(wId =>
            {
                var w = _workers.FirstOrDefault(w => w.Id == wId);
                return w?.Name?.Split(' ')[0] ?? wId;
            });
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {string.Join(", ", names)} available, returning to waiting area");

            ProcessQueue();
        }

        private void ReturnLocosBatch(List<string> locoIds)
        {
            foreach (var locoId in locoIds)
            {
                _availableLocoIds.Add(locoId);
                SimulationLogger.Instance.LogWorkerEvent(locoId, "Returned", _engine.Now);
            }

            var locoList = string.Join(", ", locoIds);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {locoList} returned to pool");

            ProcessQueue();
        }

        /// <summary>
        /// Returns a locomotive to the pool. Called explicitly by a control unit when
        /// an entity that was carrying a loco exits the system (e.g. outbound train departure).
        /// </summary>
        public void ReturnLoco(string locoId)
        {
            _availableLocoIds.Add(locoId);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: {locoId} returned to pool");
            SimulationLogger.Instance.LogWorkerEvent(locoId, "Returned", _engine.Now);
            ProcessQueue();
        }

        // ─── Queue ─────────────────────────────────────────────────────────────

        private void ProcessQueue()
        {
            if (_requestQueue.Count == 0)
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: processing queue ({_requestQueue.Count} waiting)");

            var next = _requestQueue.Peek();

            if (TryAllocateAndDispatchResources(next))
            {
                _requestQueue.Dequeue();
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss.ff} | ResourceCU: dequeued '{next.RequestId}'");
            }
        }

        // ─── Utility ───────────────────────────────────────────────────────────

        private TimeSpan CalculateWorkerTravelTime(string workerId)
        {
            var worker = _workers.FirstOrDefault(w => string.Equals(w.Id, workerId, StringComparison.OrdinalIgnoreCase));
            var speed = worker?.MovementSpeedMetersPerMinute ?? DefaultWorkerSpeedMetersPerMinute;
            if (speed <= 0) speed = DefaultWorkerSpeedMetersPerMinute;
            return TimeSpan.FromMinutes(FixedTravelDistanceMeters / speed);
        }

        private TimeSpan CalculateLocoTravelTime()
        {
            return TimeSpan.FromMinutes(FixedTravelDistanceMeters / LocoSpeedMetersPerMinute);
        }

        public List<WorkerDto> GetWorkersByIds(IEnumerable<string> ids)
        {
            var idSet = new HashSet<string>(ids.Where(x => !string.IsNullOrWhiteSpace(x)));
            return _workers.Where(w => w.Id != null && idSet.Contains(w.Id)).ToList();
        }

        public double GetWorkerTimeMultiplierForActivity(WorkerDto worker, string activityTypeKey)
        {
            if (worker.Skills == null || string.IsNullOrWhiteSpace(activityTypeKey))
                return 1.0;

            var skill = worker.Skills.FirstOrDefault(s =>
                string.Equals(s.Activity, activityTypeKey, StringComparison.OrdinalIgnoreCase));

            var mult = skill?.TimeMultiplier ?? 1.0;
            if (mult <= 0) mult = 1.0;
            return mult;
        }
    }
}