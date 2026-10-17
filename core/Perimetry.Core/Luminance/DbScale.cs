using System;
using Perimetry.Core.Config;

namespace Perimetry.Core.Luminance
{
    /// <summary>
    /// Decibel scale. Device dB is defined on the luminance INCREMENT above the background
    /// (HFA / OPI convention, approved by the clinical owner):
    ///   dB_device = 10 * log10(dLmax / dL),  dL = L_stimulus - L_background,  dLmax = L_max - L_background
    /// 0 dB is the brightest increment the device can produce.
    /// The optional HFA-equivalent value is a pure transformation for comparison only:
    ///   dB_HFA_eq = dB_device + 10 * log10(dLmax_HFA / dLmax), dLmax_HFA = 10000 asb = 10000/pi cd/m^2.
    /// </summary>
    public sealed class DbScale
    {
        public const double AsbPerCdm2 = Math.PI;

        public double BackgroundCdm2 { get; }
        public double MaxLuminanceCdm2 { get; }
        public double HfaMaxIncrementCdm2 { get; }
        public bool IsCalibrated { get; }

        public DbScale(double backgroundCdm2, double maxLuminanceCdm2, double hfaMaxStimulusAsb, bool isCalibrated)
        {
            if (maxLuminanceCdm2 <= backgroundCdm2) throw new ArgumentException("max luminance must exceed background");
            BackgroundCdm2 = backgroundCdm2;
            MaxLuminanceCdm2 = maxLuminanceCdm2;
            HfaMaxIncrementCdm2 = hfaMaxStimulusAsb / AsbPerCdm2;
            IsCalibrated = isCalibrated;
        }

        public static DbScale Nominal(DisplayConfig d) =>
            new DbScale(d.BackgroundCdm2, d.NominalMaxLuminanceCdm2, d.HfaMaxStimulusAsb, isCalibrated: false);

        public static DbScale FromCalibration(DisplayConfig d, LuminanceCalibration cal, Geometry.Eye eye) =>
            new DbScale(d.BackgroundCdm2, cal.MaxLuminance(eye), d.HfaMaxStimulusAsb, cal.IsMeasured);

        public double MaxIncrementCdm2 => MaxLuminanceCdm2 - BackgroundCdm2;

        /// <summary>dB_HFA_eq - dB_device. With nominal 100 cd/m^2 and a 10 cd/m^2 background this is ~15.5 dB.</summary>
        public double HfaOffsetDb => 10.0 * Math.Log10(HfaMaxIncrementCdm2 / MaxIncrementCdm2);

        /// <summary>Equivalent of OPI dbTocd(db, maxStim) with maxStim = device max increment.</summary>
        public double DbToIncrement(double db) => MaxIncrementCdm2 * Math.Pow(10, -db / 10.0);

        public double IncrementToDb(double incrementCdm2)
        {
            if (incrementCdm2 <= 0) throw new ArgumentOutOfRangeException(nameof(incrementCdm2));
            return -10.0 * Math.Log10(incrementCdm2 / MaxIncrementCdm2);
        }

        /// <summary>Total stimulus luminance (background + increment) for a device dB value.</summary>
        public double DbToStimulusLuminance(double db) => BackgroundCdm2 + DbToIncrement(db);

        public double ToHfaEquivalent(double deviceDb) => deviceDb + HfaOffsetDb;
        public double FromHfaEquivalent(double hfaDb) => hfaDb - HfaOffsetDb;
    }
}
