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

    public class InfrastructureRoot
    {
        [JsonPropertyName("TrackSegments")]
        public List<TrackSegmentDto>? TrackSegments { get; set; }
    }

    public class TrackSegmentDto
    {
        public int Id { get; set; }
        public int TrackId { get; set; }
        public object? MapId { get; set; }
        public double Length { get; set; }
        public int ConnectionPoint1Id { get; set; }
        public int ConnectionPoint2Id { get; set; }
        public List<int>? InterimPointIds { get; set; }
        public double Signal1DistanceToConnection1 { get; set; }
        public double Signal2DistanceToConnection2 { get; set; }
        public bool IsOccupancyTracked { get; set; }
        public string? TrackType { get; set; }
        public string? RailwayStationArea { get; set; }
        public List<int>? UndrivableSubSegmentIndexes { get; set; }

        public string GetMapIdAsString()
        {
            return MapId?.ToString() ?? "";
        }
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