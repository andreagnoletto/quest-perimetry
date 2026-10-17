using System;
using Perimetry.Core.Config;

namespace Perimetry.Core.Geometry
{
    public enum GoldmannSize { I, II, III, IV, V }

    public static class Stimulus
    {
        /// <summary>
        /// Angular diameter (degrees) of a Goldmann stimulus: the disc of the configured area
        /// (mm^2) at the Goldmann reference distance (300 mm). Size III = 4 mm^2 -> 0.431 deg.
        /// </summary>
        public static double DiameterDeg(StimulusConfig cfg, GoldmannSize size)
        {
            double area = cfg.GoldmannAreasMm2[size.ToString()];
            double radiusMm = Math.Sqrt(area / Math.PI);
            return 2.0 * Math.Atan(radiusMm / cfg.GoldmannReferenceDistanceMm) * 180.0 / Math.PI;
        }

        /// <summary>Physical radius (m) of the disc to render on a sphere of radius <paramref name="distanceM"/> so that it subtends <paramref name="diameterDeg"/>.</summary>
        public static double RenderRadiusM(double diameterDeg, double distanceM) =>
            distanceM * Math.Tan(diameterDeg / 2.0 * Math.PI / 180.0);

        /// <summary>
        /// Unit direction vector (x right, y up, z forward) for a field position in degrees.
        /// Uses the HFA bowl convention: the stimulus lies on a sphere centred on the eye; X and Y are
        /// the horizontal and vertical angular coordinates (azimuth / elevation in a Fick-like system).
        /// </summary>
        public static (double x, double y, double z) Direction(FieldPoint p)
        {
            double az = p.X * Math.PI / 180.0, el = p.Y * Math.PI / 180.0;
            double x = Math.Sin(az) * Math.Cos(el);
            double y = Math.Sin(el);
            double z = Math.Cos(az) * Math.Cos(el);
            return (x, y, z);
        }

        /// <summary>Angle (degrees) between two unit vectors.</summary>
        public static double AngleBetweenDeg((double x, double y, double z) a, (double x, double y, double z) b)
        {
            double dot = a.x * b.x + a.y * b.y + a.z * b.z;
            dot = Math.Max(-1, Math.Min(1, dot));
            return Math.Acos(dot) * 180.0 / Math.PI;
        }
    }
}
