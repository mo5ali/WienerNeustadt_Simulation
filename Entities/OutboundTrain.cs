using System;
using System.Collections.Generic;
using WienerNeustadtSimulation.Infrastructure;

namespace WienerNeustadtSimulation.Entities
{
    public class OutboundTrain
    {
        public string ID { get; set; }
        public string Destination { get; set; }
        public Track? ClassificationTrack { get; set; }
        public DateTime CreationTime { get; set; }
        public List<string> WagonGroupIds { get; set; }
        public double Length { get; set; }

        public OutboundTrain(string id, string destination, DateTime creationTime)
        {
            ID = id;
            Destination = destination;
            CreationTime = creationTime;
            WagonGroupIds = new List<string>();
            Length = 0;
        }

        public override string ToString()
        {
            string trackInfo = ClassificationTrack != null ? $", Track: {ClassificationTrack.RealLifeID}" : ", No Track";
            return $"OutboundTrain {ID} to {Destination} - {Length}m, {WagonGroupIds.Count} groups{trackInfo}";
        }
    }
}
