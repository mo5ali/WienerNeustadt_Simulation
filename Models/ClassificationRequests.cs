using System;
using WienerNeustadtSimulation.Entities;

namespace WienerNeustadtSimulation.Models
{
    /// <summary>
    /// Filed by WagonGroup after arrival on classification track
    /// </summary>
    public class WagonGroupPreparationRequest
    {
        public WagonGroup WagonGroup { get; set; }
        public DateTime RequestedAt { get; set; }

        public WagonGroupPreparationRequest(WagonGroup wagonGroup, DateTime time)
        {
            WagonGroup = wagonGroup;
            RequestedAt = time;
        }
    }

    /// <summary>
    /// Filed after securing/coupling completes - checks if train is ready
    /// </summary>
    public class CompletionCheckRequest
    {
        public string TrackId { get; set; }
        public string Destination { get; set; }
        public DateTime RequestedAt { get; set; }

        public CompletionCheckRequest(string trackId, string destination, DateTime time)
        {
            TrackId = trackId;
            Destination = destination;
            RequestedAt = time;
        }
    }

    /// <summary>
    /// Filed by OutboundTrain when ready for departure
    /// </summary>
    public class TrainDepartureRequest
    {
        public OutboundTrain Train { get; set; }
        public DateTime RequestedAt { get; set; }

        public TrainDepartureRequest(OutboundTrain train, DateTime time)
        {
            Train = train;
            RequestedAt = time;
        }
    }
}