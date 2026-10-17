using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Luminance
{
    /// <summary>
    /// Photometric calibration: per eye, a monotonic table of normalised drive level (0..1, the
    /// linear-light float sent to the display pipeline before dithering) -> measured luminance (cd/m^2).
    /// Until a measured file exists, <see cref="NominalSrgb"/> is used and every report must be
    /// flagged "não calibrado".
    /// </summary>
    public sealed class LuminanceCalibration
    {
        public sealed class Sample
        {
            public double Level { get; set; }
            public double Cdm2 { get; set; }
        }

        public sealed class EyeTable
        {
            public List<Sample> Samples { get; set; } = new List<Sample>();
        }

        public string DeviceSerial { get; set; }
        public string Photometer { get; set; }
        public DateTime? MeasuredAtUtc { get; set; }
        public DateTime? ValidUntilUtc { get; set; }
        public bool IsMeasured { get; set; }
        public Dictionary<Eye, EyeTable> Eyes { get; set; } = new Dictionary<Eye, EyeTable>();

        public static LuminanceCalibration FromJson(string json)
        {
            var cal = JsonConvert.DeserializeObject<LuminanceCalibration>(json);
            cal.Validate();
            return cal;
        }

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.Indented);

        /// <summary>sRGB transfer curve scaled to <paramref name="maxCdm2"/>, sampled on 8-bit codes. Marked as not measured.</summary>
        public static LuminanceCalibration NominalSrgb(double maxCdm2)
        {
            var cal = new LuminanceCalibration { DeviceSerial = "nominal", Photometer = "none", IsMeasured = false };
            foreach (Eye e in Enum.GetValues(typeof(Eye)))
            {
                var t = new EyeTable();
                for (int code = 0; code <= 255; code++)
                {
                    double v = code / 255.0;
                    double lin = v <= 0.04045 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
                    t.Samples.Add(new Sample { Level = v, Cdm2 = lin * maxCdm2 });
                }
                cal.Eyes[e] = t;
            }
            return cal;
        }

        public void Validate()
        {
            foreach (var kv in Eyes)
            {
                var s = kv.Value.Samples;
                if (s.Count < 2) throw new FormatException($"calibration for {kv.Key} needs at least 2 samples");
                for (int i = 1; i < s.Count; i++)
                {
                    if (s[i].Level <= s[i - 1].Level) throw new FormatException($"calibration levels for {kv.Key} must be strictly increasing");
                    if (s[i].Cdm2 < s[i - 1].Cdm2) throw new FormatException($"calibration luminance for {kv.Key} must be non-decreasing");
                }
            }
        }

        public bool IsExpired(DateTime nowUtc) => ValidUntilUtc.HasValue && nowUtc > ValidUntilUtc.Value;

        public double MaxLuminance(Eye eye) => Eyes[eye].Samples.Last().Cdm2;

        /// <summary>Luminance produced by a drive level (linear interpolation between samples).</summary>
        public double LuminanceAt(Eye eye, double level)
        {
            var s = Eyes[eye].Samples;
            if (level <= s[0].Level) return s[0].Cdm2;
            if (level >= s[s.Count - 1].Level) return s[s.Count - 1].Cdm2;
            int i = 1;
            while (s[i].Level < level) i++;
            double t = (level - s[i - 1].Level) / (s[i].Level - s[i - 1].Level);
            return s[i - 1].Cdm2 + t * (s[i].Cdm2 - s[i - 1].Cdm2);
        }

        /// <summary>
        /// Continuous drive level for a target luminance (inverse interpolation). The renderer must keep
        /// this as float and dither when quantising to the panel bit depth, so 1-2 dB steps near
        /// threshold are not lost to banding.
        /// </summary>
        public double LevelFor(Eye eye, double cdm2)
        {
            var s = Eyes[eye].Samples;
            if (cdm2 <= s[0].Cdm2) return s[0].Level;
            if (cdm2 >= s[s.Count - 1].Cdm2) return s[s.Count - 1].Level;
            int i = 1;
            while (s[i].Cdm2 < cdm2) i++;
            double span = s[i].Cdm2 - s[i - 1].Cdm2;
            if (span <= 0) return s[i].Level;
            double t = (cdm2 - s[i - 1].Cdm2) / span;
            return s[i - 1].Level + t * (s[i].Level - s[i - 1].Level);
        }
    }
}
