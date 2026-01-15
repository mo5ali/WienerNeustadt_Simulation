using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WienerNeustadtSimulation.Models
{
    public class InboundRoot
    {
        [JsonPropertyName("inboundTrains")]
        public List<TrainDto>? InboundTrains { get; set; }

        [JsonPropertyName("wagonGroups")]
        public List<WagonGroupDto>? WagonGroups { get; set; }

        [JsonPropertyName("wagons")]
        public List<WagonDto>? Wagons { get; set; }
    }

    public class TrainDto
    {
        public string? ID { get; set; }
        public double? Length { get; set; }
        public List<string>? WagonGroupIds { get; set; }
        public bool? HasLoco { get; set; }
        public string? LocomotiveId { get; set; }
        public string? Status { get; set; }
        public string? Designation { get; set; }
        public string? Time { get; set; } // ISO 8601 date-time string
    }

    public class WagonGroupDto
    {
        public string? ID { get; set; }
        public double? Length { get; set; }
        public List<string>? WagonIds { get; set; }
        public string? Destination { get; set; }
    }

    public class WagonDto
    {
        public string? ID { get; set; }
        public double? Length { get; set; }
    }
}