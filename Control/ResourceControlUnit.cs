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

            Console.WriteLine($"  → ResourceControlUnit: {_workers.Count} workers, {_shuntingLocomotives.Count} locomotives initialized");
        }

        // ─── Request submission ────────────────────────────────────────────────

        public void SubmitRequest(ResourceRequest request)
        {
            string resourceDesc = $"{request.RequiredWorkers} worker{(request.RequiredWorkers != 1 ? "s" : "")}";
            if (request.RequiresLocomotive)
                resourceDesc += " + 1 loco";

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.RequestId}' submitted ({resourceDesc})");
            SimulationLogger.Instance.LogActivityEvent(request.RequestId, "ResourceRequest", _engine.Now, "Submitted", resourceDesc);

            if (TryAllocateAndDispatchResources(request))
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.RequestId}' QUEUED (insufficient resources)");
            _requestQueue.Enqueue(request);
        }

        // ─── Allocation ────────────────────────────────────────────────────────

        private bool TryAllocateAndDispatchResources(ResourceRequest request)
        {
            Activity activity = request.Activity;

            bool needsWorkers = request.RequiredWorkers > 0;
            bool needsLoco = request.RequiresLocomotive;

            if (needsWorkers && _availableWorkerIds.Count < request.RequiredWorkers)
                return false;

            if (needsLoco && _availableLocoIds.Count == 0)
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

            // Allocate locomotive
            List<string> allocatedLocos = new List<string>();
            if (needsLoco)
            {
                var locoId = _availableLocoIds.First();
                _availableLocoIds.Remove(locoId);
                activity.AllocatedLocoIds.Add(locoId);
                allocatedLocos.Add(locoId);
            }

            // Build compact allocation message
            List<string> workerDetails = new List<string>();
            foreach (var wId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == wId);
                string firstName = worker?.Name?.Split(' ')[0] ?? wId;
                var travelTime = CalculateWorkerTravelTime(wId);
                workerDetails.Add($"{firstName}({travelTime.TotalSeconds:F0}s/{FixedTravelDistanceMeters:F0}m)");
                SimulationLogger.Instance.LogWorkerEvent(wId, "Allocated", _engine.Now, activity.ActivityId);
            }

            List<string> locoDetails = new List<string>();
            foreach (var lId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();
                locoDetails.Add($"{lId}({travelTime.TotalSeconds:F0}s/{FixedTravelDistanceMeters:F0}m)");
                SimulationLogger.Instance.LogWorkerEvent(lId, "Allocated", _engine.Now, activity.ActivityId);
            }

            var allResources = workerDetails.Concat(locoDetails).ToList();
            if (allResources.Count > 0)
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {string.Join(" ", allResources)} traveling to track {activity.Location} for '{activity.ActivityId}'");

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

            // Schedule travel for locomotive
            foreach (var locoId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnLocoArrived(activity, locoId),
                    $"LocoArrives-{locoId}-{activity.ActivityId}"
                );
            }

            return true;
        }

        // ─── Arrival callbacks ─────────────────────────────────────────────────

        private void OnWorkerArrived(Activity activity, string workerId, string workerName)
        {
            activity.RecordWorkerArrival(workerId, _engine.Now);

            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {workerName} arrived for '{activity.ActivityId}' ({arrived}/{total})");
            SimulationLogger.Instance.LogWorkerEvent(workerId, "Arrived", _engine.Now, activity.ActivityId);
        }

        private void OnLocoArrived(Activity activity, string locoId)
        {
            activity.RecordLocoArrival(locoId, _engine.Now);

            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} arrived for '{activity.ActivityId}' ({arrived}/{total})");
            SimulationLogger.Instance.LogWorkerEvent(locoId, "Arrived", _engine.Now, activity.ActivityId);
        }

        // ─── Uniform release ───────────────────────────────────────────────────

        /// <summary>
        /// The single, uniform release path. Every control unit calls this on activity
        /// completion. The activity's own declared policies (LocoStaysWithEntity,
        /// WorkersReleasedIndividually) drive all resource return behaviour.
        /// </summary>
        public void Release(Activity activity)
        {
            // ── Workers ──────────────────────────────────────────────────────────
            var workerIds = activity.AllocatedWorkerIds.ToList();
            activity.AllocatedWorkerIds.Clear();

            if (workerIds.Count > 0)
            {
                if (activity.WorkersReleasedIndividually)
                {
                    // Each worker goes back alone and is immediately dispatchable.
                    // Every ReturnWorkerInternal call triggers a queue check, so if a
                    // pending request can be satisfied by the first returning worker,
                    // it gets dispatched before the second worker is even processed.
                    foreach (var workerId in workerIds)
                        ReturnWorkerInternal(workerId);
                }
                else
                {
                    // Batch return: all workers back at once, one queue check.
                    ReturnWorkersBatch(workerIds);
                }
            }

            // ── Locomotive ───────────────────────────────────────────────────────
            if (!activity.LocoStaysWithEntity)
            {
                var locoIds = activity.AllocatedLocoIds.ToList();
                activity.AllocatedLocoIds.Clear();

                foreach (var locoId in locoIds)
                {
                    _availableLocoIds.Add(locoId);
                    Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} returned to pool");
                    SimulationLogger.Instance.LogWorkerEvent(locoId, "Returned", _engine.Now);
                    ProcessQueue();
                }
            }
            // If LocoStaysWithEntity == true, AllocatedLocoIds is deliberately left
            // intact so the caller (e.g. ClassificationCU) can read the loco ID and
            // attach it to the entity. The caller is responsible for calling ReturnLoco()
            // when the entity eventually leaves the system.
        }

        // ─── Low-level pool helpers ───────���────────────────────────────────────

        // Returns one worker and triggers a queue check. Workers are still in the
        // available pool while walking back — if a request is dispatched immediately
        // they head to the new task instead.
        private void ReturnWorkerInternal(string workerId)
        {
            _availableWorkerIds.Add(workerId);

            var worker = _workers.FirstOrDefault(w => w.Id == workerId);
            string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {firstName} available, returning to waiting area");
            SimulationLogger.Instance.LogWorkerEvent(workerId, "Returned", _engine.Now);

            ProcessQueue();
        }

        // Returns all workers at once and triggers one queue check.
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
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {string.Join(", ", names)} available, returning to waiting area");

            ProcessQueue();
        }

        /// <summary>
        /// Returns a locomotive to the pool. Called explicitly by a control unit when
        /// an entity that was carrying a loco exits the system (e.g. outbound train departure).
        /// </summary>
        public void ReturnLoco(string locoId)
        {
            _availableLocoIds.Add(locoId);
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} returned to pool");
            SimulationLogger.Instance.LogWorkerEvent(locoId, "Returned", _engine.Now);
            ProcessQueue();
        }

        // ─── Queue ─────────────────────────────────────────────────────────────

        private void ProcessQueue()
        {
            if (_requestQueue.Count == 0)
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: processing queue ({_requestQueue.Count} waiting)");

            var next = _requestQueue.Peek();

            if (TryAllocateAndDispatchResources(next))
            {
                _requestQueue.Dequeue();
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: dequeued '{next.RequestId}'");
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