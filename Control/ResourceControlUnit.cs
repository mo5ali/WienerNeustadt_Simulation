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

        // Simple assumptions for now
        private const double FixedTravelDistanceMeters = 100.0;
        private const double DefaultWorkerSpeedMetersPerMinute = 60.0;

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

        public void Submit(ResourceRequest request)
        {
            request.SubmittedAtUtc = _engine.Now;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' submitted (needs {request.RequiredWorkers} worker(s))");

            if (TryAllocateAndDispatchWorkers(request))
            {
                // Allocation done; OnFulfilled will be fired when last worker arrives.
                return;
            }

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' QUEUED (insufficient workers)");
            _requestQueue.Enqueue(request);
        }

        private bool TryAllocateAndDispatchWorkers(ResourceRequest request)
        {
            if (request.RequiredWorkers <= 0)
            {
                // Nothing to allocate; treat as immediately ready.
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' requires no workers -> ready");
                request.OnFulfilled?.Invoke(request);
                return true;
            }

            if (_availableWorkerIds.Count < request.RequiredWorkers)
                return false;

            // Allocate N workers
            var allocated = _availableWorkerIds.Take(request.RequiredWorkers).ToList();
            foreach (var id in allocated)
                _availableWorkerIds.Remove(id);

            request.AllocatedWorkerIds.Clear();
            request.ArrivedWorkerIds.Clear();

            request.AllocatedWorkerIds.AddRange(allocated);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: allocated workers [{string.Join(", ", allocated)}] to '{request.ForActivity}'");

            // Schedule travel for each worker. Commencement occurs when ALL workers arrived.
            foreach (var workerId in allocated)
            {
                var travelTime = CalculateWorkerTravelTime(workerId);

                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: worker {workerId} traveling {FixedTravelDistanceMeters}m (ETA {travelTime.TotalSeconds:F0}s) for '{request.ForActivity}'");

                _engine.Schedule(
                    _engine.Now.Add(travelTime),
                    () => OnWorkerArrived(request, workerId),
                    $"WorkerArrives-{workerId}-{request.RequestId}"
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

        private void OnWorkerArrived(ResourceRequest request, string workerId)
        {
            request.ArrivedWorkerIds.Add(workerId);

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: worker {workerId} ARRIVED for '{request.ForActivity}' ({request.ArrivedWorkerIds.Count}/{request.RequiredWorkers})");

            if (request.ArrivedWorkerIds.Count >= request.RequiredWorkers)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: all workers arrived -> '{request.ForActivity}' CAN COMMENCE");
                request.OnFulfilled?.Invoke(request);
            }
        }

        public void Release(ResourceRequest request)
        {
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: releasing resources for '{request.ForActivity}'");

            foreach (var workerId in request.AllocatedWorkerIds)
            {
                _availableWorkerIds.Add(workerId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: worker {workerId} returned to pool");
            }

            request.AllocatedWorkerIds.Clear();
            request.ArrivedWorkerIds.Clear();

            // Locos not implemented in this version; keep structure for later
            foreach (var locoId in request.AllocatedLocoIds)
            {
                _availableLocoIds.Add(locoId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: locomotive {locoId} returned to pool");
            }
            request.AllocatedLocoIds.Clear();

            ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_requestQueue.Count == 0)
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: processing queue ({_requestQueue.Count} waiting)");

            // FIFO: only try the head; if it can't be satisfied, stop.
            var next = _requestQueue.Peek();

            if (TryAllocateAndDispatchWorkers(next))
            {
                _requestQueue.Dequeue();
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: dequeued '{next.ForActivity}' (allocation/travel scheduled)");
            }
        }

        // Helper used by ArrivalCU to compute duration at commencement
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