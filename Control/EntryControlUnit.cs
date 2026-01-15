using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Models;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Infrastructure;

namespace WienerNeustadtSimulation.Control
{
    /// <summary>
    /// Manages incoming trains at entry tracks and assigns them to arrival tracks
    /// </summary>
    public class EntryControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly ArrivalControlUnit _arrivalControl;
        private readonly Queue<Train> _entryQueue;
        private readonly List<Track> _arrivalTracks;
        private readonly Dictionary<string, DateTime> _entryTimes;

        public EntryControlUnit(
            SimulationEngine engine,
            ArrivalControlUnit arrivalControl,
            List<Track> arrivalTracks)
        {
            _engine = engine;
            _arrivalControl = arrivalControl;
            _arrivalTracks = arrivalTracks;
            _entryQueue = new Queue<Train>();
            _entryTimes = new Dictionary<string, DateTime>();
        }

        public void HandleTrainArrival(TrainDto trainDto, DateTime simTimeUtc)
        {
            Console.WriteLine($"{DateTime.Now:MM/dd/yy HH:mm:ss} | {simTimeUtc:yyyy-MM-ddTHH:mm:ss'Z'} | train {trainDto.ID} arrives at entry");

            var train = CreateTrainEntity(trainDto);
            _entryQueue.Enqueue(train);
            _entryTimes[train.ID] = simTimeUtc;

            Console.WriteLine($"  → Train {train.ID} queued at entry.  Queue length: {_entryQueue.Count}");

            StartWaitingForArrivalTrack(train);
        }

        private void StartWaitingForArrivalTrack(Train train)
        {
            var assignedTrack = RequestArrivalTrack(train);

            if (assignedTrack != null && IsTrackFree(assignedTrack))
            {
                EndWaitingActivity(train, assignedTrack);
            }
            else
            {
                Console.WriteLine($"  → Train {train.ID} waiting for arrival track (retry in 30s)");

                _engine.Schedule(
                    _engine.Now.AddSeconds(30),
                    () => StartWaitingForArrivalTrack(train),
                    $"RetryArrivalTrack-{train.ID}"
                );
            }
        }

        private Track RequestArrivalTrack(Train train)
        {
            return _arrivalTracks
                .Where(t => t.Length >= train.Length && t.Designation == "Arrival")
                .OrderBy(t => t.CurrentOccupancies.Count)
                .FirstOrDefault() ?? throw new InvalidOperationException("No available arrival track found");
        }

        private bool IsTrackFree(Track track)
        {
            return track.CurrentOccupancies.Count == 0;
        }

        private void EndWaitingActivity(Train train, Track arrivalTrack)
        {
            Console.WriteLine($"  → Train {train.ID} assigned to arrival track {arrivalTrack.StationID}");

            if (_entryTimes.ContainsKey(train.ID))
            {
                var queuingTime = _engine.Now - _entryTimes[train.ID];
                Console.WriteLine($"  → Queuing time: {queuingTime.TotalMinutes: F2} minutes");
            }

            var driveTime = CalculateDriveTime(train, arrivalTrack);

            _engine.Schedule(
                _engine.Now.Add(driveTime),
                () => ArriveAtArrivalTrack(train, arrivalTrack),
                $"ArriveAtArrivalTrack-{train.ID}"
            );

            Console.WriteLine($"  → Train {train.ID} driving to arrival track (ETA: {driveTime.TotalMinutes:F1} min)");
        }

        private void ArriveAtArrivalTrack(Train train, Track arrivalTrack)
        {
            arrivalTrack.CurrentOccupancies.Add(train.ID);

            Console.WriteLine($"{DateTime.Now:MM/dd/yy HH:mm:ss} | {_engine.Now:yyyy-MM-ddTHH: mm:ss'Z'} | train {train.ID} arrives at arrival track {arrivalTrack.StationID}");

            _arrivalControl.HandleTrainOnArrivalTrack(train, arrivalTrack);
        }

        private Train CreateTrainEntity(TrainDto dto)
        {
            var train = new Train(dto.ID, dto.Length ?? 0)
            {
                WagonGroupIds = dto.WagonGroupIds ?? new List<string>(),
                HasLoco = dto.HasLoco ?? false,
                LocomotiveId = dto.LocomotiveId ?? "",
                Status = dto.Status ?? "Arriving",
                Designation = dto.Designation ?? "Inbound",
                Time = DateTime.Parse(dto.Time)
            };

            return train;
        }

        private TimeSpan CalculateDriveTime(Train train, Track track)
        {
            var distanceMeters = 500;
            var speedMetersPerMinute = 100;
            return TimeSpan.FromMinutes(distanceMeters / speedMetersPerMinute);
        }
    }
}