using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Models;

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

        public void SubmitRequest(ResourceRequest request)
        {
            string resourceDesc = $"{request.RequiredWorkers} worker{(request.RequiredWorkers != 1 ? "s" : "")}";
            if (request.RequiresLocomotive)
                resourceDesc += " + 1 loco";

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.RequestId}' submitted ({resourceDesc})");

            if (TryAllocateAndDispatchResources(request))
            {
                return;
            }

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.RequestId}' QUEUED (insufficient resources)");
            _requestQueue.Enqueue(request);
        }

        private bool TryAllocateAndDispatchResources(ResourceRequest request)
        {
            Activity activity = request.Activity;

            bool needsWorkers = request.RequiredWorkers > 0;
            bool needsLoco = request.RequiresLocomotive;

            // Check availability
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

            // Build compact allocation message with NAMES
            List<string> workerDetails = new List<string>();
            foreach (var wId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == wId);
                string firstName = worker?.Name?.Split(' ')[0] ?? wId;
                var travelTime = CalculateWorkerTravelTime(wId);
                workerDetails.Add($"{firstName}({travelTime.TotalSeconds:F0}s{FixedTravelDistanceMeters:F0}m)");
            }

            List<string> locoDetails = new List<string>();
            foreach (var lId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();
                locoDetails.Add($"{lId}({travelTime.TotalSeconds:F0}s{FixedTravelDistanceMeters:F0}m)");
            }

            // COMPACT MESSAGE: All resources in ONE line
            var allResources = workerDetails.Concat(locoDetails).ToList();
            if (allResources.Count > 0)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {string.Join(" ", allResources)} traveling to track {activity.Location} for '{activity.ActivityId}'");
            }

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

        private TimeSpan CalculateWorkerTravelTime(string workerId)
        {
            var worker = _workers.FirstOrDefault(w => string.Equals(w.Id, workerId, StringComparison.OrdinalIgnoreCase));
            var speed = worker?.MovementSpeedMetersPerMinute ?? DefaultWorkerSpeedMetersPerMinute;

            if (speed <= 0)
                speed = DefaultWorkerSpeedMetersPerMinute;

            var minutes = FixedTravelDistanceMeters / speed;
            return TimeSpan.FromMinutes(minutes);
        }

        private TimeSpan CalculateLocoTravelTime()
        {
            var minutes = FixedTravelDistanceMeters / LocoSpeedMetersPerMinute;
            return TimeSpan.FromMinutes(minutes);
        }

        private void OnWorkerArrived(Activity activity, string workerId, string workerName)
        {
            activity.RecordWorkerArrival(workerId, _engine.Now);

            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {workerName} arrived for '{activity.ActivityId}' ({arrived}/{total})");
        }

        private void OnLocoArrived(Activity activity, string locoId)
        {
            activity.RecordLocoArrival(locoId, _engine.Now);

            int arrived = activity.WorkerArrivalTimes.Count + activity.LocoArrivalTimes.Count;
            int total = activity.RequiredWorkers + (activity.RequiresLocomotive ? 1 : 0);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} arrived for '{activity.ActivityId}' ({arrived}/{total})");
        }

        public void ReturnWorker(string workerId)
        {
            _availableWorkerIds.Add(workerId);
            ProcessQueue();
        }

        public void ReturnLoco(string locoId)
        {
            _availableLocoIds.Add(locoId);
            ProcessQueue();
        }

        // NEW: Batch return workers
        public void ReturnWorkers(List<string> workerIds)
        {
            if (workerIds == null || workerIds.Count == 0)
                return;

            // Return all workers to pool
            foreach (var workerId in workerIds)
            {
                _availableWorkerIds.Add(workerId);
            }

            // Get worker names
            var workerNames = workerIds.Select(wId =>
            {
                var worker = _workers.FirstOrDefault(w => w.Id == wId);
                return worker?.Name?.Split(' ')[0] ?? wId;
            }).ToList();

            // COMPACT MESSAGE
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {string.Join(", ", workerNames)} become available, returning to waiting area");

            ProcessQueue();
        }

        public void Release(Activity activity)
        {
            // Batch return workers
            if (activity.AllocatedWorkerIds.Count > 0)
            {
                ReturnWorkers(activity.AllocatedWorkerIds.ToList());
                activity.AllocatedWorkerIds.Clear();
            }

            // Return locos
            foreach (var locoId in activity.AllocatedLocoIds)
            {
                _availableLocoIds.Add(locoId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} returned to pool");
            }

            activity.AllocatedLocoIds.Clear();

            ProcessQueue();
        }

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