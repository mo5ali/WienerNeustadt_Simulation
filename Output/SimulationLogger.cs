using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text.Json;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Output
{
    /// <summary>
    /// Buffers simulation events + metadata + entity declarations in memory and
    /// writes a single JSON document on Close().
    ///
    /// File shape (SimulationLog.json):
    ///   {
    ///     "metadata":  { generatedAtUtc, workers[], shuntingLocomotives[], capacities{} },
    ///     "entities":  { inboundTrains{id->...}, wagonGroups{id->...}, outboundTrains{id->...} },
    ///     "events":    [ { simTime, type, ... }, ... ]
    ///   }
    ///
    /// Events keep the same logical fields they had in the old CSV; the only
    /// thing that changes is the wire format. "details" stays a string for now
    /// (e.g. "entity=X;length=208,0;...") to keep the port mechanical — callers
    /// upstream do not need to change.
    /// </summary>
    public class SimulationLogger
    {
        private static SimulationLogger? _instance;
        public static SimulationLogger Instance => _instance ??= new SimulationLogger();

        private string? _logPath;
        private bool _isInitialized = false;
        private bool _metadataWritten = false;

        // ── In-memory buffers ─────────────────────────────────────────────────
        private readonly Dictionary<string, object?> _metadata = new();
        private readonly Dictionary<string, Dictionary<string, object?>> _inboundTrains = new();
        private readonly Dictionary<string, Dictionary<string, object?>> _wagonGroups = new();
        private readonly Dictionary<string, Dictionary<string, object?>> _outboundTrains = new();
        private readonly List<Dictionary<string, object?>> _events = new();

        public void Initialize(string logPath)
        {
            // Caller still passes the path so the file lands in the same OutputFiles
            // directory; we just rewrite the extension if anyone still hands us
            // "...SimulationLog.csv" (Program.cs has been updated to pass .json
            // directly, but this keeps the API forgiving).
            if (logPath.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
                logPath = Path.ChangeExtension(logPath, ".json");

            var directory = Path.GetDirectoryName(logPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            _logPath = logPath;
            _metadata.Clear();
            _inboundTrains.Clear();
            _wagonGroups.Clear();
            _outboundTrains.Clear();
            _events.Clear();

            _isInitialized = true;
            _metadataWritten = false;
        }

        /// <summary>
        /// Records static resource metadata (workers, shunt locos, capacities).
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

            _metadata["generatedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");

            var workers = (resourcePool?.Workers ?? Enumerable.Empty<WorkerDto>())
                .Where(w => !string.IsNullOrWhiteSpace(w.Id))
                .Select(w => new Dictionary<string, object?>
                {
                    ["id"] = w.Id!.Trim(),
                    ["name"] = w.Name ?? "",
                    ["movementSpeedMetersPerMinute"] = w.MovementSpeedMetersPerMinute ?? 0,
                    ["area"] = w.Area ?? "",
                })
                .ToList();
            _metadata["workers"] = workers;

            var shuntLocos = (resourcePool?.ShuntingLocomotives ?? Enumerable.Empty<ShuntingLocomotiveDto>())
                .Where(l => !string.IsNullOrWhiteSpace(l.Id))
                .Select(l => new Dictionary<string, object?>
                {
                    ["id"] = l.Id!.Trim(),
                    ["speedMetersPerMinute"] = shuntingLocoSpeedMetersPerMinute,
                })
                .ToList();
            _metadata["shuntingLocomotives"] = shuntLocos;

            _metadata["capacities"] = new Dictionary<string, object?>
            {
                ["trainLocomotives"] = trainLocoCount,
                ["exitGate"] = exitGateCount,
            };

            _metadataWritten = true;
        }

        /// <summary>
        /// Records inbound train + wagon group entity declarations.
        /// Call once after wagon group lengths have been computed.
        /// </summary>
        public void WriteInboundMetadata(InboundRoot root)
        {
            if (!_isInitialized) return;

            // Build inverse map: wgId → parentTrainId, and a wgId -> length
            // lookup so we can sum each inbound train's total length below.
            // Program.cs has already mutated WagonGroupDto.Length to reflect
            // sum-of-wagon-lengths before calling this method, so the values
            // here match what the Train entity gets at HandleTrainArrival time.
            var wgToTrain = new Dictionary<string, string>();
            var wgLength = new Dictionary<string, double>();
            foreach (var t in root.InboundTrains ?? Enumerable.Empty<TrainDto>())
            {
                if (string.IsNullOrWhiteSpace(t.ID)) continue;
                foreach (var wgId in t.WagonGroupIds ?? new List<string>())
                    wgToTrain[wgId] = t.ID!;
            }
            foreach (var wg in root.WagonGroups ?? Enumerable.Empty<WagonGroupDto>())
            {
                if (string.IsNullOrWhiteSpace(wg.ID)) continue;
                wgLength[wg.ID!] = wg.Length ?? 0;
            }

            foreach (var t in root.InboundTrains ?? Enumerable.Empty<TrainDto>())
            {
                if (string.IsNullOrWhiteSpace(t.ID)) continue;

                double trainLength = 0;
                foreach (var wgId in t.WagonGroupIds ?? new List<string>())
                    if (wgLength.TryGetValue(wgId, out var len)) trainLength += len;

                _inboundTrains[t.ID!] = new Dictionary<string, object?>
                {
                    ["id"] = t.ID,
                    ["arrivalTime"] = t.Time ?? "",
                    ["length"] = trainLength,
                    ["wagonGroupIds"] = t.WagonGroupIds ?? new List<string>(),
                    ["hasLoco"] = t.HasLoco ?? false,
                    ["locomotiveId"] = t.LocomotiveId ?? "",
                };
            }

            foreach (var wg in root.WagonGroups ?? Enumerable.Empty<WagonGroupDto>())
            {
                if (string.IsNullOrWhiteSpace(wg.ID)) continue;
                _wagonGroups[wg.ID!] = new Dictionary<string, object?>
                {
                    ["id"] = wg.ID,
                    ["length"] = wg.Length ?? 0,
                    ["destination"] = wg.Destination ?? "",
                    ["wagonIds"] = wg.WagonIds ?? new List<string>(),
                    ["parentTrainId"] = wgToTrain.TryGetValue(wg.ID!, out var pid) ? pid : "",
                };
            }
        }

        /// <summary>
        /// Records an outbound train entity as it is created during simulation.
        /// </summary>
        public void LogOutboundTrain(OutboundTrain train, DateTime simTime)
        {
            if (!_isInitialized) return;

            _outboundTrains[train.Id] = new Dictionary<string, object?>
            {
                ["id"] = train.Id,
                ["destination"] = train.Destination,
                ["trackId"] = train.CurrentTrackId,
                ["wagonGroupIds"] = train.WagonGroups.Select(wg => wg.Id).ToList(),
                // Same field name as inboundTrains[].length for symmetric
                // consumer code (sum of WG lengths on the train).
                ["length"] = train.TotalLength,
                ["totalWagonCount"] = train.TotalWagonCount,
                ["createdAt"] = simTime.ToString("yyyy-MM-ddTHH:mm:ssZ"),
            };
        }

        public void LogTrainEvent(string trainId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _events.Add(new Dictionary<string, object?>
            {
                ["simTime"] = simTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["type"] = "TrainEvent",
                ["action"] = eventName,
                ["entityId"] = trainId,
                ["details"] = details ?? "",
            });
        }

        public void LogWagonGroupEvent(string wagonGroupId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _events.Add(new Dictionary<string, object?>
            {
                ["simTime"] = simTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["type"] = "WagonGroupEvent",
                ["entityId"] = wagonGroupId,
                ["action"] = eventName,
                ["details"] = details ?? "",
            });
        }

        public void LogWorkerEvent(string workerId, string eventName, DateTime simTime, string details = "")
        {
            if (!_isInitialized) return;

            _events.Add(new Dictionary<string, object?>
            {
                ["simTime"] = simTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["type"] = "WorkerEvent",
                ["entityId"] = workerId,
                ["action"] = eventName,
                ["details"] = details ?? "",
            });
        }

        public void LogActivityEvent(string activityId, string activityType, DateTime simTime, string status, string details = "")
        {
            if (!_isInitialized) return;

            _events.Add(new Dictionary<string, object?>
            {
                ["simTime"] = simTime.ToString("yyyy-MM-ddTHH:mm:ss"),
                ["type"] = "ActivityEvent",
                ["activityId"] = activityId,
                ["activityType"] = activityType,
                ["status"] = status,
                ["details"] = details ?? "",
            });
        }

        public void Close()
        {
            if (!_isInitialized || _logPath == null)
            {
                _isInitialized = false;
                _metadataWritten = false;
                return;
            }

            var document = new Dictionary<string, object?>
            {
                ["metadata"] = _metadata,
                ["entities"] = new Dictionary<string, object?>
                {
                    ["inboundTrains"] = _inboundTrains,
                    ["wagonGroups"] = _wagonGroups,
                    ["outboundTrains"] = _outboundTrains,
                },
                ["events"] = _events,
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
            };

            var json = JsonSerializer.Serialize(document, options);
            File.WriteAllText(_logPath, json);

            _isInitialized = false;
            _metadataWritten = false;
        }
    }
}
