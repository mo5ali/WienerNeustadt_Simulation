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
        private readonly Queue<Activity> _requestQueue;

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
            _requestQueue = new Queue<Activity>();

            _workers = resourcePool.Workers ?? new List<WorkerDto>();
            _shuntingLocomotives = resourcePool.ShuntingLocomotives ?? new List<ShuntingLocomotiveDto>();

            _availableWorkerIds = new HashSet<string>(_workers.Select(w => w.Id ?? "").Where(id => !string.IsNullOrWhiteSpace(id)));
            _availableLocoIds = new HashSet<string>(_shuntingLocomotives.Select(l => l.Id ?? "").Where(id => !string.IsNullOrWhiteSpace(id)));

            Console.WriteLine($"  → ResourceControlUnit: {_workers.Count} workers, {_shuntingLocomotives.Count} locomotives initialized");
        }

        public void Submit(Activity activity)
        {
            int requiredLocos = activity.RequiresLocomotive ? 1 : 0;
            string resourceDesc = $"{activity.RequiredWorkers} worker{(activity.RequiredWorkers != 1 ? "s" : "")}";
            if (requiredLocos > 0)
                resourceDesc += $" {requiredLocos} loco";

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{activity.ActivityId}' submitted ({resourceDesc})");

            if (TryAllocateAndDispatchResources(activity))
            {
                return;
            }

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{activity.ActivityId}' QUEUED (insufficient resources)");
            _requestQueue.Enqueue(activity);
        }

        private bool TryAllocateAndDispatchResources(Activity activity)
        {
            bool needsWorkers = activity.RequiredWorkers > 0;
            bool needsLoco = activity.RequiresLocomotive;

            if (needsWorkers && _availableWorkerIds.Count < activity.RequiredWorkers)
                return false;

            if (needsLoco && _availableLocoIds.Count == 0)
                return false;

            List<string> allocatedWorkers = new List<string>();
            if (needsWorkers)
            {
                allocatedWorkers = _availableWorkerIds.Take(activity.RequiredWorkers).ToList();
                foreach (var id in allocatedWorkers)
                    _availableWorkerIds.Remove(id);
                activity.AllocatedWorkerIds.AddRange(allocatedWorkers);
            }

            List<string> allocatedLocos = new List<string>();
            if (needsLoco)
            {
                var locoId = _availableLocoIds.First();
                _availableLocoIds.Remove(locoId);
                activity.AllocatedLocoIds.Add(locoId);
                allocatedLocos.Add(locoId);
            }

            List<string> resourceNames = new List<string>();
            foreach (var wId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == wId);
                string firstName = worker?.Name?.Split(' ')[0] ?? wId;
                resourceNames.Add(firstName);
            }
            foreach (var lId in allocatedLocos)
            {
                resourceNames.Add(lId);
            }

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: allocated {string.Join(", ", resourceNames)}  to '{activity.ActivityId}'");

            foreach (var workerId in allocatedWorkers)
            {
                var worker = _workers.FirstOrDefault(w => w.Id == workerId);
                string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
                var travelTime = CalculateWorkerTravelTime(workerId);

                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {firstName} traveling to track {activity.Location} ({FixedTravelDistanceMeters:F0}m {travelTime.TotalSeconds:F0}s) for '{activity.ActivityId}'");

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnWorkerArrived(activity, workerId, firstName),
                    $"WorkerArrives-{workerId}-{activity.ActivityId}"
                );
            }

            foreach (var locoId in allocatedLocos)
            {
                var travelTime = CalculateLocoTravelTime();

                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} driving to track {activity.Location} ({FixedTravelDistanceMeters:F0}m {travelTime.TotalSeconds:F0}s) for '{activity.ActivityId}'");

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

        // Return individual worker to pool
        public void ReturnWorker(string workerId)
        {
            _availableWorkerIds.Add(workerId);
            ProcessQueue();
        }

        // Return individual loco to pool
        public void ReturnLoco(string locoId)
        {
            _availableLocoIds.Add(locoId);
            ProcessQueue();
        }

        // Release all resources from an activity
        public void Release(Activity activity)
        {
            foreach (var workerId in activity.AllocatedWorkerIds)
            {
                _availableWorkerIds.Add(workerId);
                var worker = _workers.FirstOrDefault(w => w.Id == workerId);
                string firstName = worker?.Name?.Split(' ')[0] ?? workerId;
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {firstName} returned to pool");
            }

            foreach (var locoId in activity.AllocatedLocoIds)
            {
                _availableLocoIds.Add(locoId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: {locoId} returned to pool");
            }

            activity.AllocatedWorkerIds.Clear();
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
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: dequeued '{next.ActivityId}'");
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