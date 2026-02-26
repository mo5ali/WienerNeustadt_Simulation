using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Models
{
    public enum RequestPriority
    {
        Normal = 0,
        High = 1,
        Critical = 2
    }

    public enum RequestStatus
    {
        Pending,
        Queued,
        Assigned,
        InProgress,
        Completed,
        Rejected
    }

    public static class ActivityTypes
    {
        public const string IncomingTrainPreparation = "IncomingTrainPreparation";
        public const string PushOff = "PushOff";
        public const string Securing = "Securing";
        public const string Coupling = "Coupling";

        // Abbreviations for request IDs
        private static readonly Dictionary<string, string> Abbrev = new()
        {
            { IncomingTrainPreparation, "ITP" },
            { PushOff, "PO" },
            { Securing, "SEC" },
            { Coupling, "COP" },
        };

        public static string GetAbbreviation(string activityType)
            => Abbrev.TryGetValue(activityType, out var v) ? v : "REQ";
    }

    public enum ResourceType
    {
        Worker,
        Supervisor,
        ShuntingLocomotive,
        SecuringEquipment,
        CouplingEquipment
    }

    public sealed class ResourceRequirement
    {
        public ResourceType Type { get; }
        public int Quantity { get; }

        public ResourceRequirement(ResourceType type, int quantity)
        {
            if (quantity <= 0) throw new ArgumentException("Quantity must be > 0", nameof(quantity));
            Type = type;
            Quantity = quantity;
        }

        public override string ToString() => $"{Quantity}x{Type}";
    }

    /// <summary>
    /// A resource request that can block the caller chain until fulfilled.
    /// </summary>
    public class ResourceRequest
    {
        public string RequestId { get; }
        public string Sender { get; }
        public string Handler { get; } = "ResourceControlUnit";

        public string ForEntity { get; }          // Train.ID or WagonGroup.ID (string)
        public string ActivityType { get; }       // e.g. IncomingTrainPreparation
        public string ForActivity { get; }        // unique instance id: IncomingTrainPreparation_{ts}_{ForEntity}

        public string LocationTrackId { get; }    // Track.StationID
        public string LocationArea { get; }       // Track.Area

        public RequestPriority Priority { get; set; } = RequestPriority.Normal;
        public List<ResourceRequirement> ResourcesRequested { get; } = new();

        public RequestStatus Status { get; internal set; } = RequestStatus.Pending;

        /// <summary>
        /// Called by ResourceControlUnit when the resources are assigned and available at location.
        /// This callback is how the blocked process chain resumes.
        /// </summary>
        public Action<ResourceRequest>? OnFulfilled { get; set; }

        public ResourceRequest(
            string sender,
            string forEntity,
            string activityType,
            string locationTrackId,
            string locationArea = "")
        {
            Sender = sender;
            ForEntity = forEntity;
            ActivityType = activityType;
            LocationTrackId = locationTrackId;
            LocationArea = locationArea ?? string.Empty;

            var timestampSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var abbr = ActivityTypes.GetAbbreviation(activityType);

            // Examples:
            // RequestId: ITP_1700000000_12345
            // ForActivity: IncomingTrainPreparation_1700000000_12345
            RequestId = $"{abbr}_{timestampSeconds}_{forEntity}";
            ForActivity = $"{activityType}_{timestampSeconds}_{forEntity}";
        }

        public void AddResource(ResourceType type, int quantity)
            => ResourcesRequested.Add(new ResourceRequirement(type, quantity));

        public override string ToString()
        {
            var res = string.Join(", ", ResourcesRequested);
            return $"{RequestId} / for {ForEntity} / Track {LocationTrackId} / [{res}] / {Status}";
        }
    }
}