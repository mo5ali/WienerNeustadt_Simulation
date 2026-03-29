using System;
using System.IO;
using System.Linq;
using System.Globalization;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Output
{
    /// <summary>
    /// High-performance CSV logger for simulation events
    /// </summary>
    public class SimulationLogger
    {
        private static SimulationLogger _instance;
        public static SimulationLogger Instance => _instance ??= new SimulationLogger();

        private StreamWriter _writer;
        private bool _isInitialized = false;
        private bool _metadataWritten = false;

        public void Initialize(string logPath)
        {
            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _writer = new StreamWriter(logPath, false);

            // Write CSV header
            _writer.WriteLine("EventType;EntityId;Event;SimTime;Details");
            _writer.Flush();

            _isInitialized = true;
            _metadataWritten = false;
        }

        /// <summary>
        /// Writes static resource metadata into the same CSV as comment-style lines.
        /// These lines start with '#' so parsers can ignore them easily.
        ///
        /// Call once after Initialize() and after loading ResourcePool.json.
        /// </summary>
        public void WriteResourceMetadata(
            ResourcePoolRoot resourcePool,
            int trainLocoCount = 100,
            int exitGateCount = 1,
            double shuntingLocoSpeedMetersPerMinute = 25.0
        )
        {
            if (!_isInitialized) return;
            if (_metadataWritten) return;

            _writer.WriteLine("#META;ResourcePool;v1");
            _writer.WriteLine($"#META;GeneratedAtUtc;{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}");

            // Workers (these fields exist in your WorkerDto usage in ResourceControlUnit)
            var workers = resourcePool?.Workers ?? Enumerable.Empty<WorkerDto>();
            foreach (var w in workers.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                var id = w.Id!.Trim();
                var name = (w.Name ?? "").Replace(";", ",").Trim();
                var speed = w.MovementSpeedMetersPerMinute ?? 0;

                // #WORKER;<Id>;<Name>;<SpeedMetersPerMinute>
                _writer.WriteLine($"#WORKER;{id};{name};{speed.ToString(CultureInfo.InvariantCulture)}");
            }

            // Shunting locomotives:
            // Your ShuntingLocomotiveDto doesn't have Name/MovementSpeedMetersPerMinute.
            // We log Id and a fixed speed (matches ResourceControlUnit constant behavior).
            var locos = resourcePool?.ShuntingLocomotives ?? Enumerable.Empty<ShuntingLocomotiveDto>();
            foreach (var l in locos.Where(x => !string.IsNullOrWhiteSpace(x.Id)))
            {
                var id = l.Id!.Trim();

                // #SHUNTLOCO;<Id>;<DisplayName>;<SpeedMetersPerMinute>
                _writer.WriteLine($"#SHUNTLOCO;{id};{id};{shuntingLocoSpeedMetersPerMinute.ToString(CultureInfo.InvariantCulture)}");
            }

            // Global capacities / synthetic resources used by sim
            _writer.WriteLine($"#CAPACITY;TrainLocomotives;{trainLocoCount}");
            _writer.WriteLine($"#CAPACITY;ExitGate;{exitGateCount}");

            _writer.WriteLine("#ENDMETA");
            _writer.Flush();

            _metadataWritten = true;
        }

        public void LogTrainEvent(string trainId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"{simTime:yyyy-MM-ddTHH:mm:ss};TrainEvent;{eventName};{trainId};{details}");
            _writer.Flush();
        }

        public void LogWagonGroupEvent(string wagonGroupId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"{simTime:yyyy-MM-ddTHH:mm:ss};WagonGroupEvent;{wagonGroupId};{eventName};{details}");
            _writer.Flush();
        }

        public void LogWorkerEvent(string workerId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"{simTime:yyyy-MM-ddTHH:mm:ss};WorkerEvent;{workerId};{eventName};{details}");
            _writer.Flush();
        }

        public void LogActivityEvent(string activityId, string activityType, DateTime simTime, string status, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"{simTime:yyyy-MM-ddTHH:mm:ss};ActivityEvent;{activityId};{activityType};{status}|{details}");
            _writer.Flush();
        }

        public void Close()
        {
            if (_writer != null)
            {
                _writer.Close();
                _writer.Dispose();
                _writer = null;
            }

            _isInitialized = false;
            _metadataWritten = false;
        }
    }
}