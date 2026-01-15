using System;
using System.Collections.Generic;
using System.Linq;
using WienerNeustadtSimulation.Engine;
using WienerNeustadtSimulation.Entities;
using WienerNeustadtSimulation.Infrastructure;
using WienerNeustadtSimulation.Models;

namespace WienerNeustadtSimulation.Control
{
    /// <summary>
    /// Manages wagon groups on classification tracks and outbound train formation
    /// </summary>
    public class ClassificationControlUnit
    {
        private readonly SimulationEngine _engine;
        private readonly Dictionary<Track, OutboundTrain> _formingTrains;
        private readonly Dictionary<string, DateTime> _wgEntryTimes;
        private const double MIN_OUTBOUND_TRAIN_LENGTH = 300.0; // meters

        public ClassificationControlUnit(SimulationEngine engine)
        {
            _engine = engine;
            _formingTrains = new Dictionary<Track, OutboundTrain>();
            _wgEntryTimes = new Dictionary<string, DateTime>();
        }

        public void CreateWagonGroupEntity(string wgId, WagonGroupDto wgData, Track classificationTrack)
        {
            Console.WriteLine($"  ✓ WagonGroup entity {wgId} created on track {classificationTrack.StationID}");

            _wgEntryTimes[wgId] = _engine.Now;

            if (classificationTrack.CurrentOccupancies.Count == 0)
            {
                CreateOutboundTrainEntity(classificationTrack, wgData.Destination ?? "Unknown");
                RequestSecuring(wgId, classificationTrack);
            }
            else
            {
                RequestCoupling(wgId, classificationTrack);
            }

            classificationTrack.CurrentOccupancies.Add(wgId);

            WaitOnTrack(wgId, wgData, classificationTrack);
        }

        private void RequestSecuring(string wgId, Track track)
        {
            Console.WriteLine($"    → Securing WG {wgId} (first on track {track.StationID})");

            var securingTime = TimeSpan.FromMinutes(2);

            _engine.Schedule(
                _engine.Now.Add(securingTime),
                () => Console.WriteLine($"    ✓ WG {wgId} secured"),
                $"SecuringComplete-{wgId}"
            );
        }

        private void RequestCoupling(string wgId, Track track)
        {
            var backmostWgId = track.CurrentOccupancies.LastOrDefault();

            Console.WriteLine($"    → Coupling WG {wgId} to backmost WG {backmostWgId} on track {track.StationID}");

            var couplingTime = TimeSpan.FromMinutes(3);

            _engine.Schedule(
                _engine.Now.Add(couplingTime),
                () => Console.WriteLine($"    ✓ WG {wgId} coupled"),
                $"CouplingComplete-{wgId}"
            );
        }

        private void WaitOnTrack(string wgId, WagonGroupDto wgData, Track track)
        {
            if (_formingTrains.ContainsKey(track))
            {
                var train = _formingTrains[track];
                train.WagonGroupIds.Add(wgId);
                train.Length += wgData.Length ?? 0;

                Console.WriteLine($"    → WG {wgId} added to forming outbound train.  Train length: {train.Length:F1}m, WGs: {train.WagonGroupIds.Count}");

                CheckTrainCompletionCondition(track);
            }
        }

        private void CreateOutboundTrainEntity(Track track, string destination)
        {
            var trainId = GenerateOutboundTrainId();

            var train = new OutboundTrain(trainId, destination, _engine.Now)
            {
                ClassificationTrack = track,
                WagonGroupIds = new List<string>(),
                Length = 0
            };

            _formingTrains[track] = train;

            Console.WriteLine($"  ✓ Outbound train entity {trainId} created for destination '{destination}' on track {track.StationID}");
        }

        private void CheckTrainCompletionCondition(Track track)
        {
            if (!_formingTrains.ContainsKey(track))
                return;

            var train = _formingTrains[track];

            bool isComplete = train.Length >= MIN_OUTBOUND_TRAIN_LENGTH;

            if (isComplete)
            {
                Console.WriteLine($"  ✓ Outbound train {train.ID} completion condition met!");
                RequestLeavingActivity(train, track);
            }
        }

        private void RequestLeavingActivity(OutboundTrain train, Track track)
        {
            Console.WriteLine($"  → Requesting leaving activity for outbound train {train.ID}");

            var waitTime = TimeSpan.FromMinutes(5);

            _engine.Schedule(
                _engine.Now.Add(waitTime),
                () => ResourcesForLeavingReady(train, track),
                $"LeavingResourcesReady-{train.ID}"
            );
        }

        private void ResourcesForLeavingReady(OutboundTrain train, Track track)
        {
            Console.WriteLine($"  → Resources ready for train {train.ID}");

            var couplingTime = TimeSpan.FromMinutes(4);

            _engine.Schedule(
                _engine.Now.Add(couplingTime),
                () => LocomotiveCoupled(train, track),
                $"LocomotiveCoupled-{train.ID}"
            );
        }

        private void LocomotiveCoupled(OutboundTrain train, Track track)
        {
            Console.WriteLine($"  → Locomotive coupled to train {train.ID}");

            var preparationTime = TimeSpan.FromMinutes(12);

            _engine.Schedule(
                _engine.Now.Add(preparationTime),
                () => LeavingPreparationComplete(train, track),
                $"LeavingPrepComplete-{train.ID}"
            );
        }

        private void LeavingPreparationComplete(OutboundTrain train, Track track)
        {
            Console.WriteLine($"  → Leaving preparation complete for train {train.ID}");
            Console.WriteLine($"    → Braketest ✓ Documents ✓ Permissions ✓");

            var driveTime = TimeSpan.FromMinutes(6);

            _engine.Schedule(
                _engine.Now.Add(driveTime),
                () => RequestDepartureActivity(train, track),
                $"DrivingToExit-{train.ID}"
            );
        }

        private void RequestDepartureActivity(OutboundTrain train, Track track)
        {
            Console.WriteLine($"  → Requesting departure for train {train.ID}");

            var departureTime = TimeSpan.FromMinutes(2);

            _engine.Schedule(
                _engine.Now.Add(departureTime),
                () => TrainExitsSystem(train, track),
                $"Departure-{train.ID}"
            );
        }

        private void TrainExitsSystem(OutboundTrain train, Track track)
        {
            Console.WriteLine($"\n{DateTime.Now:MM/dd/yy HH:mm:ss} | {_engine.Now:yyyy-MM-ddTHH:mm: ss'Z'} | ✓ Outbound train {train.ID} exits system to destination '{train.Destination}'");

            var formationTime = _engine.Now - train.CreationTime;
            Console.WriteLine($"  → Formation time: {formationTime.TotalMinutes:F2} minutes");
            Console.WriteLine($"  → Final composition:  {train.WagonGroupIds.Count} wagon groups, {train.Length:F1}m total");

            foreach (var wgId in train.WagonGroupIds)
            {
                track.CurrentOccupancies.Remove(wgId);

                if (_wgEntryTimes.ContainsKey(wgId))
                {
                    var dwellTime = _engine.Now - _wgEntryTimes[wgId];
                    Console.WriteLine($"    → WG {wgId} dwell time on classification track: {dwellTime.TotalMinutes:F2} minutes");
                }
            }

            _formingTrains.Remove(track);

            Console.WriteLine($"  ✗ Outbound train entity {train.ID} destroyed (exited system)\n");
        }

        private string GenerateOutboundTrainId()
        {
            var random = new Random();
            return $"OUT{random.Next(10000, 99999)}";
        }
    }
}