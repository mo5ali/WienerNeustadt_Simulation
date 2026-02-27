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

        // Resource pools
        private readonly List<WorkerDto> _workers;
        private readonly List<ShuntingLocomotiveDto> _shuntingLocomotives;

        // Available resource tracking
        private readonly HashSet<string> _availableWorkerIds;
        private readonly HashSet<string> _availableLocoIds;

        public ResourceControlUnit(SimulationEngine engine, ResourcePoolRoot resourcePool)
        {
            _engine = engine;
            _requestQueue = new Queue<ResourceRequest>();

            _workers = resourcePool.Workers ?? new List<WorkerDto>();
            _shuntingLocomotives = resourcePool.ShuntingLocomotives ?? new List<ShuntingLocomotiveDto>();

            // Initialize all resources as available
            _availableWorkerIds = new HashSet<string>(_workers.Select(w => w.Id ?? ""));
            _availableLocoIds = new HashSet<string>(_shuntingLocomotives.Select(l => l.Id ?? ""));

            Console.WriteLine($"  → ResourceControlUnit: {_workers.Count} workers, {_shuntingLocomotives.Count} locomotives initialized");
        }

        public void Submit(ResourceRequest request)
        {
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' submitted");

            // Try to allocate resources
            bool canFulfill = TryAllocateResources(request);

            if (canFulfill)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' FULFILLED immediately");
                request.OnFulfilled?.Invoke(request);
            }
            else
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: request '{request.ForActivity}' QUEUED (resources unavailable)");
                _requestQueue.Enqueue(request);
            }
        }

        private bool TryAllocateResources(ResourceRequest request)
        {
            // For now: simple check if we have at least 1 worker available
            // Later: check specific skills, multiple workers, locomotives, etc.

            if (_availableWorkerIds.Count > 0)
            {
                // Allocate one worker (simple version)
                var workerId = _availableWorkerIds.First();
                _availableWorkerIds.Remove(workerId);

                // Store allocation so we can release later
                if (!request.AllocatedWorkerIds.Contains(workerId))
                    request.AllocatedWorkerIds.Add(workerId);

                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: allocated worker {workerId} to '{request.ForActivity}'");

                return true;
            }

            return false;
        }

        public void Release(ResourceRequest request)
        {
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: releasing resources for '{request.ForActivity}'");

            // Return allocated workers to the pool
            foreach (var workerId in request.AllocatedWorkerIds)
            {
                _availableWorkerIds.Add(workerId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: worker {workerId} returned to pool");
            }
            request.AllocatedWorkerIds.Clear();

            // Return allocated locomotives to the pool
            foreach (var locoId in request.AllocatedLocoIds)
            {
                _availableLocoIds.Add(locoId);
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: locomotive {locoId} returned to pool");
            }
            request.AllocatedLocoIds.Clear();

            // Try to fulfill queued requests
            ProcessQueue();
        }

        private void ProcessQueue()
        {
            if (_requestQueue.Count == 0)
                return;

            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: processing queue ({_requestQueue.Count} waiting)");

            // Try to fulfill the first queued request
            var nextRequest = _requestQueue.Peek();

            if (TryAllocateResources(nextRequest))
            {
                _requestQueue.Dequeue();
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: queued request '{nextRequest.ForActivity}' now FULFILLED");
                nextRequest.OnFulfilled?.Invoke(nextRequest);

                // Try to process more (recursive)
                ProcessQueue();
            }
        }
    }
}