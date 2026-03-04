using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WienerNeustadtSimulation.Models
{
    public class ActivityRegistry
    {
        private static ActivityRegistry? _instance;
        private static readonly object _lock = new object();

        private readonly Dictionary<string, Activity> _activities;

        private ActivityRegistry()
        {
            _activities = new Dictionary<string, Activity>();
        }

        public static ActivityRegistry Instance
        {
            get
            {
                if (_instance == null)
                {
                    lock (_lock)
                    {
                        _instance ??= new ActivityRegistry();
                    }
                }
                return _instance;
            }
        }

        public void Register(Activity activity)
        {
            _activities[activity.ActivityId] = activity;
        }

        public Activity? GetActivity(string activityId)
        {
            return _activities.ContainsKey(activityId) ? _activities[activityId] : null;
        }

        public List<Activity> GetAllActivities()
        {
            return _activities.Values.ToList();
        }

        public List<Activity> GetActivitiesForEntity(string entityId)
        {
            return _activities.Values.Where(a => a.EntityId == entityId).ToList();
        }

        public List<Activity> GetActivitiesByType(string type)
        {
            return _activities.Values.Where(a => a.ActivityType == type).ToList();
        }

        public List<Activity> GetActivitiesByControlUnit(string controlUnit)
        {
            return _activities.Values.Where(a => a.ControlUnit == controlUnit).ToList();
        }

        public void ExportToJson(string filePath)
        {
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            };

            var activityData = _activities.Values.Select(a => new
            {
                a.ActivityId,
                a.ActivityType,
                a.EntityId,
                a.EntityLength,
                a.Location,
                a.Area,
                a.ControlUnit,
                RequestedAt = a.RequestedAt.ToString("yyyy-MM-ddTHH:mm:ss"),
                AllResourcesArrivedAt = a.AllResourcesArrivedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                CommencedAt = a.CommencedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                ScheduledCompletionAt = a.ScheduledCompletionAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                CompletedAt = a.CompletedAt?.ToString("yyyy-MM-ddTHH:mm:ss"),
                a.AllocatedWorkerIds,
                a.AllocatedLocoIds,
                CalculatedDurationMinutes = a.CalculatedDuration?.TotalMinutes,
                a.AverageWorkerMultiplier,
                RequiredWorkers = a.RequiredWorkers,
                RequiresLocomotive = a.RequiresLocomotive,
                BaseSecondsPerMeter = a.BaseSecondsPerMeter,
                ActivityClass = a.GetType().Name,
                // Driving-specific
                Speed = a is DrivingActivity drive ? (double?)drive.Speed : null
            }).ToList();

            string json = JsonSerializer.Serialize(activityData, options);
            File.WriteAllText(filePath, json);

            Console.WriteLine($"\n[ActivityRegistry] Exported {_activities.Count} activities to {filePath}");
        }

        public void PrintSummary()
        {
            Console.WriteLine($"\n╔════════════════════════════════════════════════════════════╗");
            Console.WriteLine($"║              ACTIVITY REGISTRY SUMMARY                     ║");
            Console.WriteLine($"╚════════════════════════════════════════════════════════════╝");
            Console.WriteLine($"  Total Activities: {_activities.Count}");

            var byType = _activities.Values.GroupBy(a => a.ActivityType);
            Console.WriteLine($"\n  By Type:");
            foreach (var group in byType.OrderBy(g => g.Key))
            {
                Console.WriteLine($"    {group.Key}: {group.Count()}");
            }

            var byCU = _activities.Values.GroupBy(a => a.ControlUnit);
            Console.WriteLine($"\n  By Control Unit:");
            foreach (var group in byCU.OrderBy(g => g.Key))
            {
                Console.WriteLine($"    {group.Key}: {group.Count()}");
            }

            var completed = _activities.Values.Count(a => a.CompletedAt.HasValue);
            var inProgress = _activities.Values.Count(a => a.CommencedAt.HasValue && !a.CompletedAt.HasValue);
            var waiting = _activities.Values.Count(a => !a.CommencedAt.HasValue);

            Console.WriteLine($"\n  Status Breakdown:");
            Console.WriteLine($"    Completed: {completed}");
            Console.WriteLine($"    In Progress: {inProgress}");
            Console.WriteLine($"    Waiting for Resources: {waiting}");
            Console.WriteLine();
        }

        public void Clear()
        {
            _activities.Clear();
        }
    }
}