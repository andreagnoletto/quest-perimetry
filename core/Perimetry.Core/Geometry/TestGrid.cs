using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Perimetry.Core.Config;

namespace Perimetry.Core.Geometry
{
    public sealed class GridLocation
    {
        public int Index { get; }
        /// <summary>Position in OD convention.</summary>
        public FieldPoint Point { get; }
        /// <summary>True for the two points adjacent to the physiological blind spot (excluded from indices, as on the HFA).</summary>
        public bool IsBlindSpot { get; }
        public bool IsAnchor { get; }

        public GridLocation(int index, FieldPoint point, bool isBlindSpot, bool isAnchor)
        {
            Index = index; Point = point; IsBlindSpot = isBlindSpot; IsAnchor = isAnchor;
        }

        public override string ToString() => $"#{Index} {Point}{(IsBlindSpot ? " BS" : "")}{(IsAnchor ? " anchor" : "")}";
    }

    /// <summary>A test pattern (24-2, 30-2, 10-2, 24-2C or custom), stored in OD convention.</summary>
    public sealed class TestGrid
    {
        public string Id { get; }
        public string Name { get; }
        public IReadOnlyList<GridLocation> Locations { get; }
        public double NeighbourRadiusDeg { get; }
        public string Source { get; }
        /// <summary>False when the grid does not cover the GHT sectors (10-2): GHT is reported as not applicable.</summary>
        public bool GhtApplicable { get; }

        private readonly List<int>[] _neighbours;

        public TestGrid(string id, string name, IEnumerable<GridLocation> locations, double neighbourRadiusDeg, string source, bool ghtApplicable = true)
        {
            Id = id; Name = name; Source = source; GhtApplicable = ghtApplicable;
            Locations = locations.ToList();
            NeighbourRadiusDeg = neighbourRadiusDeg;
            if (Locations.Select(l => l.Point).Distinct().Count() != Locations.Count)
                throw new ArgumentException($"Grid {id} has duplicated points");
            _neighbours = Locations.Select(a => Locations
                    .Where(b => b.Index != a.Index && a.Point.DistanceTo(b.Point) <= neighbourRadiusDeg + 1e-9)
                    .Select(b => b.Index).ToList())
                .ToArray();
        }

        public int Count => Locations.Count;

        /// <summary>Locations used for global indices (blind-spot neighbours removed).</summary>
        public IEnumerable<GridLocation> AnalysisLocations => Locations.Where(l => !l.IsBlindSpot);

        public IReadOnlyList<int> NeighboursOf(int index) => _neighbours[index];

        public int? IndexOf(FieldPoint p)
        {
            var loc = Locations.FirstOrDefault(l => l.Point.Equals(p));
            return loc?.Index;
        }

        public static TestGrid FromConfig(GridConfig cfg)
        {
            var anchors = (cfg.Anchors ?? new List<GridAnchorConfig>()).Select(a => new FieldPoint(a.X, a.Y)).ToList();
            var locs = cfg.Points.Select((p, i) =>
            {
                var fp = new FieldPoint(p.X, p.Y);
                return new GridLocation(i, fp, p.BlindSpot, anchors.Any(a => a.Equals(fp)));
            }).ToList();
            if (anchors.Count > 0 && locs.Count(l => l.IsAnchor) != anchors.Count)
                throw new InvalidDataException($"Grid {cfg.Id}: every anchor must be a grid point");
            return new TestGrid(cfg.Id, cfg.Name, locs, cfg.NeighbourRadiusDeg, cfg.Source, cfg.GhtApplicable);
        }

        /// <summary>
        /// Custom grid from CSV with header "x,y" (degrees, OD convention), optional third column "blindSpot" (0/1).
        /// If no anchors are given, the four points closest to (±9, ±9) are used, one per quadrant.
        /// </summary>
        public static TestGrid FromCsv(string id, string csv, double neighbourRadiusDeg = 9.0)
        {
            var lines = csv.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            if (lines.Count == 0) throw new InvalidDataException("empty CSV");
            int start = char.IsLetter(lines[0][0]) ? 1 : 0;
            var pts = new List<(FieldPoint p, bool bs)>();
            for (int i = start; i < lines.Count; i++)
            {
                var f = lines[i].Split(',', ';');
                if (f.Length < 2) throw new InvalidDataException($"CSV line {i + 1}: expected x,y");
                double x = double.Parse(f[0], CultureInfo.InvariantCulture);
                double y = double.Parse(f[1], CultureInfo.InvariantCulture);
                bool bs = f.Length > 2 && (f[2].Trim() == "1" || f[2].Trim().Equals("true", StringComparison.OrdinalIgnoreCase));
                pts.Add((new FieldPoint(x, y), bs));
            }
            var anchorIdx = new HashSet<int>();
            foreach (var (sx, sy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                var target = new FieldPoint(9 * sx, 9 * sy);
                var cand = pts.Select((p, i) => (p, i))
                    .Where(t => Math.Sign(t.p.p.X) == sx && Math.Sign(t.p.p.Y) == sy && !t.p.bs)
                    .OrderBy(t => t.p.p.DistanceTo(target)).ToList();
                if (cand.Count > 0) anchorIdx.Add(cand[0].i);
            }
            var locs = pts.Select((p, i) => new GridLocation(i, p.p, p.bs, anchorIdx.Contains(i)));
            return new TestGrid(id, id, locs, neighbourRadiusDeg, "custom CSV");
        }
    }

    public static class GridLibrary
    {
        public static TestGrid Get(ClinicalConfig cfg, string id)
        {
            if (!cfg.Grids.TryGetValue(id, out var g))
                throw new KeyNotFoundException($"Unknown grid '{id}'. Available: {string.Join(", ", cfg.Grids.Keys)}");
            return TestGrid.FromConfig(g);
        }
    }
}
