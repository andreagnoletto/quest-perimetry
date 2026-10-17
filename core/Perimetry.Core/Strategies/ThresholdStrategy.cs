using System;
using System.Collections.Generic;
using System.Linq;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Strategies
{
    /// <summary>
    /// Runs one procedure per grid location, interleaved in random order, with an HFA-like growth
    /// pattern: the anchor points (one per quadrant, ~(±9, ±9)) start from the age-expected value;
    /// when a location finishes, its untested neighbours are started from their expected value
    /// shifted by the mean total deviation of the finished neighbours.
    /// </summary>
    public sealed class ThresholdStrategy
    {
        public enum LocationState { NotStarted, Active, Finished }

        public TestGrid Grid { get; }
        public ProcedureFactory Factory { get; }
        private readonly double[] _expected;
        private readonly bool _seed;
        private readonly Rng _rng;
        private readonly ILocationProcedure[] _procs;
        private readonly LocationState[] _state;
        private readonly double[] _startValue;
        private int _last = -1;

        public ThresholdStrategy(TestGrid grid, ProcedureFactory factory, IReadOnlyList<double> expectedDeviceDb, bool useNeighbourSeeding, Rng rng)
        {
            if (expectedDeviceDb.Count != grid.Count) throw new ArgumentException("expected values must match the grid");
            Grid = grid; Factory = factory; _expected = expectedDeviceDb.ToArray(); _rng = rng;
            _seed = useNeighbourSeeding && factory.Kind != StrategyKind.Screening && grid.Locations.Any(l => l.IsAnchor);
            _procs = new ILocationProcedure[grid.Count];
            _state = new LocationState[grid.Count];
            _startValue = Enumerable.Repeat(double.NaN, grid.Count).ToArray();

            foreach (var loc in grid.Locations)
                if (!_seed || loc.IsAnchor) Activate(loc.Index, _expected[loc.Index]);
        }

        private void Activate(int i, double start)
        {
            _startValue[i] = start;
            _procs[i] = Factory.Create(start);
            _state[i] = _procs[i].IsFinished ? LocationState.Finished : LocationState.Active;
        }

        public bool IsFinished => _state.All(s => s == LocationState.Finished);
        public LocationState StateOf(int i) => _state[i];
        public ILocationProcedure ProcedureOf(int i) => _procs[i];
        public double StartValueOf(int i) => _startValue[i];
        public double ExpectedOf(int i) => _expected[i];

        /// <summary>Chooses the next location at random among active ones, avoiding an immediate repeat when possible.</summary>
        public int? PickNextLocation()
        {
            var active = Enumerable.Range(0, _state.Length).Where(i => _state[i] == LocationState.Active).ToList();
            if (active.Count == 0)
            {
                // Isolated points that no finished neighbour reached: start them from the global mean deviation.
                var pending = Enumerable.Range(0, _state.Length).Where(i => _state[i] == LocationState.NotStarted).ToList();
                if (pending.Count == 0) return null;
                double shift = MeanDeviationOf(Enumerable.Range(0, _state.Length));
                foreach (var i in pending) Activate(i, _expected[i] + shift);
                return PickNextLocation();
            }
            if (active.Count > 1) active.Remove(_last);
            _last = active[_rng.NextInt(0, active.Count)];
            return _last;
        }

        public double NextStimulusDb(int i) => _procs[i].NextStimulusDb();

        public void Update(int i, double stimulusDb, bool seen)
        {
            if (_state[i] != LocationState.Active) throw new InvalidOperationException($"location {i} is not active");
            _procs[i].Update(stimulusDb, seen);
            if (!_procs[i].IsFinished) return;
            _state[i] = LocationState.Finished;
            if (!_seed) return;
            foreach (var n in Grid.NeighboursOf(i))
            {
                if (_state[n] != LocationState.NotStarted) continue;
                double shift = MeanDeviationOf(Grid.NeighboursOf(n));
                Activate(n, _expected[n] + shift);
            }
        }

        private double MeanDeviationOf(IEnumerable<int> idx)
        {
            var d = idx.Where(j => _state[j] == LocationState.Finished && !double.IsNaN(_procs[j].FinalDb)
                                   && !Grid.Locations[j].IsBlindSpot)
                .Select(j => _procs[j].FinalDb - _expected[j]).ToList();
            return d.Count == 0 ? 0 : d.Average();
        }
    }
}
