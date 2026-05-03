using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace WienerNeustadtSimulation.Models
{
    public class ResourcePoolRoot
    {
        [JsonPropertyName("workers")]
        public List<WorkerDto>? Workers { get; set; }

        [JsonPropertyName("shuntingLocomotives")]
        public List<ShuntingLocomotiveDto>? ShuntingLocomotives { get; set; }
    }

    public class WorkerDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("movementSpeedMetersPerMinute")]
        public double? MovementSpeedMetersPerMinute { get; set; }

        [JsonPropertyName("skills")]
        public List<WorkerSkillDto>? Skills { get; set; }

        // Which yard area this worker is assigned to: "Arrival" or "Classification".
        // Mirrors real-world practice — walking 800 m between areas mid-shift is
        // unrealistic, so workers are localized to one side. Null / empty means
        // the worker is area-agnostic (back-compat with old data files).
        [JsonPropertyName("area")]
        public string? Area { get; set; }
    }

    public class WorkerSkillDto
    {
        [JsonPropertyName("activity")]
        public string? Activity { get; set; }

        [JsonPropertyName("timeMultiplier")]
        public double? TimeMultiplier { get; set; }
    }

    public class ShuntingLocomotiveDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("maxSpeed")]
        public double? MaxSpeed { get; set; }
    }

    // ResourceRequest class used by ResourceControlUnit
  
}