using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Engine
{
    public class ScheduledEvent
    {
        public DateTime SimTime { get; }
        public Action Action { get; }
        public string Description { get; }

        public ScheduledEvent(DateTime simTime, Action action, string description = "")
        {
            SimTime = simTime.ToUniversalTime();
            Action = action ?? throw new ArgumentNullException(nameof(action));
            Description = description ?? "";
        }
    }

    // Minimal discrete-event engine: keeps a sorted list of scheduled events and executes them in time order.
    public class SimulationEngine
    {
        private readonly List<ScheduledEvent> _events = new List<ScheduledEvent>();
        public DateTime Now { get; private set; } = DateTime.MinValue;

        // Schedule an action at a simulation time.
        public void Schedule(DateTime simTime, Action action, string description = "")
        {
            var ev = new ScheduledEvent(simTime, action, description);
            _events.Add(ev);
            // Keep list sorted by SimTime (stable by insertion order for equal times)
            _events.Sort((a, b) =>
            {
                var c = a.SimTime.CompareTo(b.SimTime);
                if (c != 0) return c;
                return 0;
            });
        }

        // Run until no more events.
        public void Run()
        {
            while (_events.Count > 0)
            {
                var ev = _events[0];
                _events.RemoveAt(0);
                Now = ev.SimTime;
                try
                {
                    ev.Action();
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error executing event '{ev.Description}' at {ev.SimTime:o}: {ex.Message}");
                }
            }
        }
    }
}