using System;

namespace Perimetry.Core.Geometry
{
    /// <summary>
    /// Angular geometry for a fixation target that is not straight ahead (oculokinetic mode B).
    /// A field position is always a RETINAL offset from the fixation target. When the target is at
    /// direction T (its own field coordinates from straight ahead), the stimulus for retinal offset p
    /// is placed by rotating the straight-ahead direction of p with the rotation R that takes the
    /// forward axis (0,0,1) onto T along the shortest arc (Rodrigues' formula, axis = forward x T).
    /// This is the rotation an eye obeying Listing's law performs to look at T, so the stimulus lands
    /// on the same retinal position whatever the target position, and eccentricity from the target
    /// is preserved exactly (<see cref="Eccentricity"/>).
    /// Directions follow <see cref="Stimulus.Direction"/> (x right, y up, z forward; X = azimuth,
    /// Y = elevation).
    /// </summary>
    public static class AngularGeometry
    {
        private const double Deg = Math.PI / 180.0;

        /// <summary>Absolute (from straight ahead) field position of retinal offset <paramref name="relative"/> when the target is at <paramref name="target"/>.</summary>
        public static FieldPoint Compose(FieldPoint target, FieldPoint relative)
        {
            if (Math.Abs(target.X) < 1e-12 && Math.Abs(target.Y) < 1e-12) return relative;
            var t = Stimulus.Direction(target);
            var v = Stimulus.Direction(relative);
            return FromDirection(Rotate(t, v, false));
        }

        /// <summary>Inverse of <see cref="Compose"/>: the retinal offset of absolute position <paramref name="absolute"/> for a target at <paramref name="target"/>.</summary>
        public static FieldPoint Relative(FieldPoint target, FieldPoint absolute)
        {
            if (Math.Abs(target.X) < 1e-12 && Math.Abs(target.Y) < 1e-12) return absolute;
            var t = Stimulus.Direction(target);
            var v = Stimulus.Direction(absolute);
            return FromDirection(Rotate(t, v, true));
        }

        /// <summary>Angle (degrees) between the directions of two field positions.</summary>
        public static double AngleBetweenDeg(FieldPoint a, FieldPoint b) =>
            Stimulus.AngleBetweenDeg(Stimulus.Direction(a), Stimulus.Direction(b));

        /// <summary>Eccentricity (degrees) of a field position from straight ahead, as an angle on the sphere.</summary>
        public static double Eccentricity(FieldPoint p) => AngleBetweenDeg(p, new FieldPoint(0, 0));

        /// <summary>Field coordinates (azimuth X, elevation Y, degrees) of a unit direction.</summary>
        public static FieldPoint FromDirection((double x, double y, double z) d)
        {
            double el = Math.Asin(Math.Max(-1, Math.Min(1, d.y)));
            double az = Math.Atan2(d.x, d.z);
            return new FieldPoint(Round(az / Deg), Round(el / Deg));
        }

        // Rotation taking forward (0,0,1) onto t (inverse = false) or t back onto forward (inverse = true), applied to v.
        private static (double x, double y, double z) Rotate((double x, double y, double z) t, (double x, double y, double z) v, bool inverse)
        {
            // axis k = forward x t = (-t.y, t.x, 0); angle a = acos(t.z)
            double kx = -t.y, ky = t.x, kz = 0;
            double kn = Math.Sqrt(kx * kx + ky * ky);
            if (kn < 1e-15) return t.z > 0 ? v : (-v.x, v.y, -v.z); // target straight ahead or straight behind
            kx /= kn; ky /= kn;
            double cos = Math.Max(-1, Math.Min(1, t.z));
            double sin = inverse ? -kn : kn; // |forward x t| = sin(a) (kn already equals sin a)
            // Rodrigues: v' = v cos + (k x v) sin + k (k . v)(1 - cos)
            double cx = ky * v.z - kz * v.y, cy = kz * v.x - kx * v.z, cz = kx * v.y - ky * v.x;
            double dot = kx * v.x + ky * v.y + kz * v.z;
            return (v.x * cos + cx * sin + kx * dot * (1 - cos),
                    v.y * cos + cy * sin + ky * dot * (1 - cos),
                    v.z * cos + cz * sin + kz * dot * (1 - cos));
        }

        private static double Round(double v) => Math.Round(v, 9);
    }
}
