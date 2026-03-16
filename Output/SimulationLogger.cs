using System;
using System.IO;

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
        }

        public void LogTrainEvent(string trainId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"TrainEvent;{trainId};{eventName};{simTime:yyyy-MM-ddTHH:mm:ss};{details}");
            _writer.Flush();
        }

        public void LogWagonGroupEvent(string wagonGroupId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"WagonGroupEvent;{wagonGroupId};{eventName};{simTime:yyyy-MM-ddTHH:mm:ss};{details}");
            _writer.Flush();
        }

        public void LogWorkerEvent(string workerId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"WorkerEvent;{workerId};{eventName};{simTime:yyyy-MM-ddTHH:mm:ss};{details}");
            _writer.Flush();
        }

        public void LogActivityEvent(string activityId, string activityType, DateTime simTime, string status, string details = "")
        {
            if (!_isInitialized) return;

            _writer.WriteLine($"ActivityEvent;{activityId};{activityType};{simTime:yyyy-MM-ddTHH:mm:ss};{status}|{details}");
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
        }
    }
}