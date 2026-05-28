using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using WienerNeustadtSimulation.Entities;
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
                var area = (w.Area ?? "").Replace(";", ",").Trim();

                // #WORKER;<Id>;<Name>;<SpeedMetersPerMinute>;<Area>
                // Area is optional — empty string means the worker is area-agnostic.
                _writer.WriteLine($"#WORKER;{id};{name};{speed.ToString(CultureInfo.InvariantCulture)};{area}");
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

        /// <summary>
        /// Writes inbound train and wagon group entity metadata into the CSV.
        /// Call once after wagon group lengths have been computed.
        /// </summary>
        public void WriteInboundMetadata(InboundRoot root)
        {
            if (!_isInitialized) return;

            // Build inverse map: wgId → parentTrainId
            var wgToTrain = new Dictionary<string, string>();
            foreach (var t in root.InboundTrains ?? Enumerable.Empty<TrainDto>())
            {
                if (string.IsNullOrWhiteSpace(t.ID)) continue;
                foreach (var wgId in t.WagonGroupIds ?? new List<string>())
                    wgToTrain[wgId] = t.ID!;
            }

            foreach (var t in root.InboundTrains ?? Enumerable.Empty<TrainDto>())
            {
                if (string.IsNullOrWhiteSpace(t.ID)) continue;
                var wgIds = string.Join("|", t.WagonGroupIds ?? new List<string>());
                var hasLoco = (t.HasLoco ?? false).ToString();
                var locoId = (t.LocomotiveId ?? "").Replace(";", ",");
                // #INTRAIN;<id>;<arrivalTimeISO>;<wgIds_pipe_sep>;<hasLoco>;<locoId>
                _writer.WriteLine($"#INTRAIN;{t.ID};{t.Time ?? ""};{wgIds};{hasLoco};{locoId}");
            }

            foreach (var wg in root.WagonGroups ?? Enumerable.Empty<WagonGroupDto>())
            {
                if (string.IsNullOrWhiteSpace(wg.ID)) continue;
                var length = (wg.Length ?? 0).ToString(CultureInfo.InvariantCulture);
                var dest = (wg.Destination ?? "").Replace(";", ",");
                var wagonIds = string.Join("|", wg.WagonIds ?? new List<string>());
                var parentId = wgToTrain.TryGetValue(wg.ID!, out var pid) ? pid : "";
                // #WAGONGROUP;<id>;<lengthMeters>;<destination>;<wagonIds_pipe_sep>;<parentTrainId>
                _writer.WriteLine($"#WAGONGROUP;{wg.ID};{length};{dest};{wagonIds};{parentId}");
            }

            _writer.Flush();
        }

        /// <summary>
        /// Logs an outbound train entity as it is created during simulation.
        /// </summary>
        public void LogOutboundTrain(OutboundTrain train, DateTime simTime)
        {
            if (!_isInitialized) return;
            var wgIds = string.Join("|", train.WagonGroups.Select(wg => wg.Id));
            var length = train.TotalLength.ToString(CultureInfo.InvariantCulture);
            // #OUTTRAIN;<id>;<destination>;<trackId>;<wgIds_pipe_sep>;<totalLength>;<totalWagonCount>;<createdAtISO>
            _writer.WriteLine($"#OUTTRAIN;{train.Id};{train.Destination};{train.CurrentTrackId};{wgIds};{length};{train.TotalWagonCount};{simTime:yyyy-MM-ddTHH:mm:ssZ}");
            _writer.Flush();
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