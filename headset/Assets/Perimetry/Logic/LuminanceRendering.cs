using System;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;

namespace Perimetry.Headset.Logic
{
    public enum DitherMode { None, Bayer4x4, Bayer4x4Temporal }

    /// <summary>
    /// Ordered dithering of the continuous drive level to the panel bit depth. The drive level is the
    /// sRGB-ENCODED value (what <see cref="LuminanceCalibration"/> tables are indexed by): code =
    /// floor(level * maxCode + threshold), threshold from a 4x4 Bayer matrix. Averaged over a 4x4 tile
    /// (and, in temporal mode, over 16 frames per pixel) the code mean equals level * maxCode, so steps much
    /// finer than one code survive quantisation. The shader Perimetry/MonocularUnlit implements the same
    /// formula and outputs sRGBToLinear(code / maxCode) into an sRGB render target.
    /// </summary>
    public static class Dither
    {
        private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

        public static DitherMode Parse(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "none": return DitherMode.None;
                case "bayer4x4": return DitherMode.Bayer4x4;
                case "bayer4x4temporal": return DitherMode.Bayer4x4Temporal;
                default: throw new FormatException("unknown dithering mode " + s);
            }
        }

        /// <summary>Threshold in [0,1) for a pixel; temporal mode shifts the pattern so each pixel visits all 16 thresholds every 16 frames.</summary>
        public static double Threshold(int px, int py, int frame, DitherMode mode)
        {
            if (mode == DitherMode.None) return 0.5;
            int ox = 0, oy = 0;
            if (mode == DitherMode.Bayer4x4Temporal) { ox = frame & 3; oy = (frame >> 2) & 3; }
            int x = (px + ox) & 3, y = (py + oy) & 3;
            return (Bayer[y * 4 + x] + 0.5) / 16.0;
        }

        public static int Code(double level, int px, int py, int frame, int bits, DitherMode mode)
        {
            int max = (1 << bits) - 1;
            double v = Math.Max(0, Math.Min(1, level)) * max;
            return Math.Min(max, (int)Math.Floor(v + Threshold(px, py, frame, mode)));
        }

        public static double SrgbToLinear(double v) => v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
        public static double LinearToSrgb(double l) => l <= 0.0031308 ? l * 12.92 : 1.055 * Math.Pow(l, 1 / 2.4) - 0.055;
    }

    /// <summary>
    /// Converts a requested luminance into the drive level for one eye, through the measured calibration
    /// when present or the nominal sRGB curve otherwise (exam then flagged "NÃO CALIBRADO").
    /// </summary>
    public sealed class LuminanceMapper
    {
        private readonly LuminanceCalibration _cal;

        public LuminanceMapper(DisplayConfig display, LuminanceCalibration measured = null)
        {
            Display = display;
            _cal = measured ?? LuminanceCalibration.NominalSrgb(display.NominalMaxLuminanceCdm2);
            IsCalibrated = measured != null && measured.IsMeasured;
            DitherMode = Dither.Parse(display.Dithering);
        }

        public DisplayConfig Display { get; }
        public bool IsCalibrated { get; }
        public DitherMode DitherMode { get; }
        public LuminanceCalibration Calibration => _cal;
        public string CalibrationRef => IsCalibrated ? _cal.DeviceSerial + "@" + _cal.MeasuredAtUtc?.ToString("o") : null;

        public double LevelFor(Eye eye, double cdm2) => _cal.LevelFor(eye, cdm2);

        public DbScale ScaleFor(Eye eye) => IsCalibrated ? DbScale.FromCalibration(Display, _cal, eye) : DbScale.Nominal(Display);

        /// <summary>Mean luminance actually produced by dithered rendering of <paramref name="cdm2"/> over a 4x4 tile and 16 frames.</summary>
        public double RenderedMeanCdm2(Eye eye, double cdm2)
        {
            double level = LevelFor(eye, cdm2);
            int bits = Display.PanelBitsPerChannel, max = (1 << bits) - 1;
            int frames = DitherMode == DitherMode.Bayer4x4Temporal ? 16 : 1;
            double sum = 0; int n = 0;
            for (int f = 0; f < frames; f++)
                for (int y = 0; y < 4; y++)
                    for (int x = 0; x < 4; x++)
                    {
                        sum += _cal.LuminanceAt(eye, Dither.Code(level, x, y, f, bits, DitherMode) / (double)max);
                        n++;
                    }
            return sum / n;
        }
    }
}
