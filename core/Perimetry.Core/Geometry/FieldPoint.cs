using System;

namespace Perimetry.Core.Geometry
{
    /// <summary>
    /// A location in the visual field, in degrees from the fixation point, as seen by the patient:
    /// +X = right, +Y = superior. Grids are stored in the OD convention (temporal = +X, blind spot
    /// at (+15, -1.5)); for OS the X coordinate is mirrored (see <see cref="ForEye"/>).
    /// </summary>
    public readonly struct FieldPoint : IEquatable<FieldPoint>
    {
        public double X { get; }
        public double Y { get; }

        public FieldPoint(double x, double y) { X = x; Y = y; }

        public double Eccentricity => Math.Sqrt(X * X + Y * Y);

        public double DistanceTo(FieldPoint o) => Math.Sqrt((X - o.X) * (X - o.X) + (Y - o.Y) * (Y - o.Y));

        /// <summary>Converts an OD-convention point to the given eye's field coordinates.</summary>
        public FieldPoint ForEye(Eye eye) => eye == Eye.OD ? this : new FieldPoint(-X, Y);

        public FieldPoint Offset(double dx, double dy) => new FieldPoint(X + dx, Y + dy);

        public bool Equals(FieldPoint o) => Math.Abs(X - o.X) < 1e-9 && Math.Abs(Y - o.Y) < 1e-9;
        public override bool Equals(object obj) => obj is FieldPoint p && Equals(p);
        public override int GetHashCode() => (Math.Round(X * 1000), Math.Round(Y * 1000)).GetHashCode();
        public override string ToString() => FormattableString.Invariant($"({X:0.##}, {Y:0.##})");
    }
}
