using System;
using System.Collections.Generic;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Represents the priority level of a resource request
    /// </summary>
    public enum RequestPriority
    {
        Normal = 0,
        High = 1,
        Critical = 2
    }

    /// <summary>
    /// Activity type constants and their abbreviations
    /// </summary>
    public static class ActivityTypes
    {
        public const string IncomingTrainPreparation = "IncomingTrainPreparation";
        public const string PushOff = "PushOff";
        public const string Securing = "Securing";
        public const string Coupling = "Coupling";
        public const string DeparturePreparation = "DeparturePreparation";
        public const string Inspection = "Inspection";

        // Abbreviations for request IDs
        public static readonly Dictionary<string, string> Abbreviations = new Dictionary<string, string>
        {
            { IncomingTrainPreparation, "ITP" },
            { PushOff, "PO" },
            { Securing, "SEC" },
            { Coupling, "COP" },
            { DeparturePreparation, "DP" },
            { Inspection, "INS" }
        };

        public static string GetAbbreviation(string activityType)
        {
            return Abbreviations.ContainsKey(activityType) ? Abbreviations[activityType] : "ACT";
        }
    }

    /// <summary>
    /// Types of resources that can be requested
    /// </summary>
    public enum ResourceType
    {
        Worker,
        Supervisor,
        ShuntingLocomotive,
        SecuringEquipment,
        CouplingEquipment,
        InspectionEquipment
    }

    /// <summary>
    /// Represents a single resource requirement (type and quantity)
    /// </summary>
    public class ResourceRequirement
    {
        public ResourceType Type { get; set; }
        public int Quantity { get; set; }

        public ResourceRequirement(ResourceType type, int quantity)
        {
            Type = type;
            Quantity = quantity;
        }

        public override string ToString()
        {
            return $"({Quantity}) {Type}";
        }
    }

    /// <summary>
    /// Base class for all resource requests sent to ResourceControlUnit
    /// </summary>
    public class ResourceRequest
    {
        public string RequestId { get; private set; }
        public string Sender { get; set; } // e.g., "ArrivalControlUnit"
        public string Handler { get; set; } // e.g., "ResourceControlUnit"
        public string ForEntity { get; set; } // e.g., Train ID "12345" or WagonGroup ID "1234501"
        public string ForActivity { get; set; } // Unique instance ID e.g., "IncomingTrainPreparation_1046180226_4578"
        public string ActivityType { get; private set; } // e.g., "IncomingTrainPreparation"
        public string LocationTrackId { get; set; } // Track.StationID (4-digit)
        public string LocationArea { get; set; } // Track.Area
        public RequestPriority Priority { get; set; }
        public List<ResourceRequirement> ResourcesRequested { get; set; }
        public DateTime RequestTime { get; set; }
        public RequestStatus Status { get; set; }

        public ResourceRequest(
            string sender,
            string forEntity,
            string activityType,
            string locationTrackId,
            string locationArea = "")
        {
            Sender = sender;
            Handler = "ResourceControlUnit";
            ForEntity = forEntity;
            ActivityType = activityType;
            LocationTrackId = locationTrackId;
            LocationArea = locationArea;
            Priority = RequestPriority.Normal;
            ResourcesRequested = new List<ResourceRequirement>();
            RequestTime = DateTime.UtcNow;
            Status = RequestStatus.Pending;

            // Generate unique IDs based on activity type and entity
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string abbreviation = ActivityTypes.GetAbbreviation(activityType);
            
            RequestId = $"{abbreviation}_{timestamp}_{forEntity}";
            ForActivity = $"{activityType}_{timestamp}_{forEntity}";
        }

        public void AddResource(ResourceType type, int quantity)
        {
            ResourcesRequested.Add(new ResourceRequirement(type, quantity));
        }

        public override string ToString()
        {
            string resources = string.Join(", ", ResourcesRequested);
            return $"Request {RequestId} from {Sender} for activity {ForActivity} on entity {ForEntity} " +
                   $"at Track {LocationTrackId} | Resources: {resources} | Status: {Status}";
        }
    }

    /// <summary>
    /// Status of a resource request
    /// </summary>
    public enum RequestStatus
    {
        Pending,
        Searching,
        Assigned,
        InProgress,
        Completed,
        Failed
    }
}