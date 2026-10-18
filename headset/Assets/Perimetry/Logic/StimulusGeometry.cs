using System;
using Perimetry.Core.Geometry;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Places stimuli in head (camera) space, in angular coordinates centred on the TESTED eye.
    /// Unity convention: x right, y up, z forward, metres. The fixation target sits straight ahead of the
    /// tested eye; a field point (x°, y°) lies on the sphere of radius <c>distance</c> centred on that eye
    /// (HFA bowl geometry, <see cref="Stimulus.Direction"/>). Using the head centre instead of the eye would
    /// shift every point by about atan(IPD/2 / distance) = 1.8° at 1 m for a 63 mm IPD.
    /// </summary>
    public static class StimulusPlacement
    {
        public struct Placement
        {
            /// <summary>Disc centre in head space (m).</summary>
            public double X, Y, Z;
            /// <summary>Unit vector from the eye to the disc centre; the disc faces back along it.</summary>
            public double DirX, DirY, DirZ;
            /// <summary>Radius of the (round) disc so that it subtends the requested angle from the eye.</summary>
            public double RadiusM;
        }

        public static Placement Place(double fieldX, double fieldY, double diameterDeg, (double x, double y, double z) eyeInHead, double distanceM)
        {
            if (distanceM <= 0) throw new ArgumentOutOfRangeException(nameof(distanceM));
            var d = Stimulus.Direction(new FieldPoint(fieldX, fieldY));
            return new Placement
            {
                X = eyeInHead.x + d.x * distanceM,
                Y = eyeInHead.y + d.y * distanceM,
                Z = eyeInHead.z + d.z * distanceM,
                DirX = d.x, DirY = d.y, DirZ = d.z,
                RadiusM = Stimulus.RenderRadiusM(diameterDeg, distanceM),
            };
        }

        /// <summary>
        /// Angular diameter (deg) of a disc of radius <paramref name="radiusM"/> centred at <paramref name="centre"/>
        /// and perpendicular to the line of sight, as seen from <paramref name="eye"/>.
        /// </summary>
        public static double SubtendedDeg((double x, double y, double z) eye, (double x, double y, double z) centre, double radiusM)
        {
            double dx = centre.x - eye.x, dy = centre.y - eye.y, dz = centre.z - eye.z;
            double dist = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            return 2 * Math.Atan(radiusM / dist) * 180 / Math.PI;
        }
    }

    /// <summary>Triangle-fan disc in the XY plane (normal -Z, facing a viewer on the -Z side), unit area-equivalent radius.</summary>
    public static class DiscMesh
    {
        /// <summary>
        /// Vertex 0 is the centre. The polygon radius is enlarged so the polygon has the same AREA as the
        /// unit circle (Goldmann sizes are defined by area); with 64 segments the factor is 1.0008.
        /// </summary>
        public static (double[] xy, int[] triangles) Build(int segments = 64)
        {
            if (segments < 8) throw new ArgumentOutOfRangeException(nameof(segments));
            double k = AreaEquivalentFactor(segments);
            var xy = new double[(segments + 1) * 2];
            for (int i = 0; i < segments; i++)
            {
                double a = 2 * Math.PI * i / segments;
                xy[(i + 1) * 2] = k * Math.Cos(a);
                xy[(i + 1) * 2 + 1] = k * Math.Sin(a);
            }
            var tris = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                // clockwise when seen from -Z (Unity front face winding)
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % segments;
                tris[i * 3 + 2] = 1 + i;
            }
            return (xy, tris);
        }

        public static double AreaEquivalentFactor(int segments) =>
            Math.Sqrt(2 * Math.PI / (segments * Math.Sin(2 * Math.PI / segments)));

        /// <summary>Area of the polygon described by <paramref name="xy"/> (vertex 0 = centre).</summary>
        public static double PolygonArea(double[] xy)
        {
            int n = xy.Length / 2 - 1;
            double a = 0;
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                a += xy[(i + 1) * 2] * xy[(j + 1) * 2 + 1] - xy[(j + 1) * 2] * xy[(i + 1) * 2 + 1];
            }
            return Math.Abs(a) / 2;
        }
    }
}
