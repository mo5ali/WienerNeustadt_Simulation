using System;
using System.Collections.Generic;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Control
{
    /// <summary>
    /// Minimal resource manager:
    /// - Holds simple counts per ResourceType
    /// - Accepts ResourceRequests
    /// - If available: allocates immediately and calls request.OnFulfilled
    /// - If not: queues and retries periodically
    /// </summary>
    public class ResourceControlUnit
    {
        private readonly SimulationEngine _engine;

        private readonly Dictionary<ResourceType, int> _available = new();
        private readonly Queue<ResourceRequest> _queue = new();

        private readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(30);

        public ResourceControlUnit(SimulationEngine engine, Dictionary<ResourceType, int> initialInventory)
        {
            _engine = engine;

            foreach (var kv in initialInventory)
                _available[kv.Key] = kv.Value;
        }

        public void Submit(ResourceRequest request)
        {
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | Resource req: {request}");

            if (TryAllocate(request))
            {
                Fulfill(request);
                return;
            }

            request.Status = RequestStatus.Queued;
            _queue.Enqueue(request);

            // schedule a retry loop (lightweight)
            _engine.Schedule(
                _engine.Now.Add(_retryInterval),
                () => RetryQueuedRequests(),
                $"RetryResources-{request.RequestId}"
            );
        }

        /// <summary>
        /// Must be called when the activity is done so resources can be reused.
        /// </summary>
        public void Release(ResourceRequest request)
        {
            foreach (var rr in request.ResourcesRequested)
                _available[rr.Type] = GetAvailable(rr.Type) + rr.Quantity;

            Console.WriteLine($"  ← Resources released for {request.RequestId}");

            // Opportunistic retry right away
            RetryQueuedRequests();
        }

        private void RetryQueuedRequests()
        {
            if (_queue.Count == 0)
                return;

            // Preserve order; cycle through once and re-queue those still blocked.
            var n = _queue.Count;
            for (var i = 0; i < n; i++)
            {
                var req = _queue.Dequeue();

                if (TryAllocate(req))
                {
                    Fulfill(req);
                }
                else
                {
                    _queue.Enqueue(req);
                }
            }

            // If still pending requests, schedule next retry
            if (_queue.Count > 0)
            {
                _engine.Schedule(
                    _engine.Now.Add(_retryInterval),
                    () => RetryQueuedRequests(),
                    $"RetryResources-Queue"
                );
            }
        }

        private bool TryAllocate(ResourceRequest request)
        {
            // Check all required types are available
            foreach (var rr in request.ResourcesRequested)
            {
                if (GetAvailable(rr.Type) < rr.Quantity)
                    return false;
            }

            // Allocate (decrement)
            foreach (var rr in request.ResourcesRequested)
                _available[rr.Type] = GetAvailable(rr.Type) - rr.Quantity;

            request.Status = RequestStatus.Assigned;
            return true;
        }

        private void Fulfill(ResourceRequest request)
        {
            Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: Resource allocated: {request.RequestId}");

            // Resume the blocked chain:
            if (request.OnFulfilled == null)
            {
                Console.WriteLine($"{_engine.Now:dd/MM/yyyy-HH:mm:ss} | ResourceCU: Request {request.RequestId} has no OnFulfilled callback; nothing to resume.");
                return;
            }

            request.OnFulfilled(request);
        }

        private int GetAvailable(ResourceType t) => _available.TryGetValue(t, out var v) ? v : 0;
    }
}