using System;
using System.Collections.Generic;
using System.Linq;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Analysis
{
    /// <summary>
    /// Index formulas shared by the analysis and the normative builder (so normals and patients are
    /// processed identically). Arrays are indexed by grid location; blind-spot locations are skipped.
    /// </summary>
    public static class GlobalIndices
    {
        /// <summary>
        /// General height: the TD value at rank floor((1 - perc) * n) in decreasing order (HFA: 85th
        /// percentile = 7th best of 52 points). Port of visualFields::ghdef.
        /// </summary>
        public static double GeneralHeight(IReadOnlyList<double> td, IReadOnlyList<int> idx, double perc)
        {
            var sorted = idx.Select(i => td[i]).OrderByDescending(v => v).ToArray();
            int pos = (int)Math.Floor((1 - perc) * sorted.Length); // 1-based rank as in R
            return sorted[Math.Max(1, pos) - 1];
        }

        /// <summary>
        /// Mean deviation. "inverseVariance" = Heijl et al. 1987 / HFA:
        ///   MD = sum(TD_i / s_i^2) / sum(1 / s_i^2);
        /// "inverseSd" = visualFields (weights 1/s_i).
        /// </summary>
        public static double MeanDeviation(IReadOnlyList<double> td, IReadOnlyList<double> sd, IReadOnlyList<int> idx, string weighting)
        {
            bool invVar = !string.Equals(weighting, "inverseSd", StringComparison.OrdinalIgnoreCase);
            double num = 0, den = 0;
            foreach (int i in idx)
            {
                double w = invVar ? 1.0 / (sd[i] * sd[i]) : 1.0 / sd[i];
                num += td[i] * w;
                den += w;
            }
            return num / den;
        }

        /// <summary>
        /// Pattern standard deviation, Heijl et al. 1987 (HFA):
        ///   PSD = sqrt( (1/n) sum s_i^2  *  1/(n-1) sum (TD_i - MD)^2 / s_i^2 ).
        /// </summary>
        public static double PatternStandardDeviation(IReadOnlyList<double> td, IReadOnlyList<double> sd, IReadOnlyList<int> idx, double md)
        {
            int n = idx.Count;
            double meanVar = idx.Sum(i => sd[i] * sd[i]) / n;
            double s = idx.Sum(i => (td[i] - md) * (td[i] - md) / (sd[i] * sd[i])) / (n - 1);
            return Math.Sqrt(meanVar * s);
        }

        /// <summary>GHT score of one point from its pattern deviation probability.</summary>
        public static double GhtPointScore(double pdProbability, IReadOnlyList<GhtPointScore> scores)
        {
            foreach (var s in scores.OrderBy(s => s.P))
                if (pdProbability <= s.P + 1e-12) return s.Score;
            return 0;
        }

        /// <summary>Sector-pair resolution: for each sector, the grid indices of the upper and the mirrored lower points (missing points are skipped).</summary>
        public static List<(int[] upper, int[] lower)> ResolveSectors(GhtConfig ght, TestGrid grid)
        {
            var r = new List<(int[], int[])>();
            foreach (var s in ght.Sectors)
            {
                var up = s.Points.Select(p => grid.IndexOf(new FieldPoint(p[0], p[1]))).Where(i => i.HasValue).Select(i => i.Value).ToArray();
                var lo = s.Points.Select(p => grid.IndexOf(new FieldPoint(p[0], -p[1]))).Where(i => i.HasValue).Select(i => i.Value).ToArray();
                r.Add((up, lo));
            }
            return r;
        }

        /// <summary>Upper - lower score difference per sector.</summary>
        public static double[] GhtSectorDifferences(IReadOnlyList<double> pdProb, List<(int[] upper, int[] lower)> sectors, IReadOnlyList<GhtPointScore> scores)
        {
            return sectors.Select(s =>
                s.upper.Sum(i => GhtPointScore(pdProb[i], scores)) - s.lower.Sum(i => GhtPointScore(pdProb[i], scores))).ToArray();
        }

        /// <summary>
        /// Visual Field Index (Bengtsson &amp; Heijl 2008), as implemented in visualFields::vfcomputevfi:
        /// per-location score 100 * (1 - |TD| / age-normal sensitivity) when the location is significant
        /// at p &lt; 5% on the PD map (TD map when MD &lt; -20 dB), otherwise 100; sensitivity &lt; 0 -> 0;
        /// eccentricity weights w = 1 / (a (d + b)) (cortical magnification).
        /// </summary>
        public static double Vfi(TestGrid grid, IReadOnlyList<int> idx, double md, IReadOnlyList<double> sens, IReadOnlyList<double> normal,
            IReadOnlyList<double> td, IReadOnlyList<double> tdP, IReadOnlyList<double> pdP, VfiConfig cfg)
        {
            bool usePd = md >= cfg.UsePdWhenMdAtLeastDb;
            double num = 0, den = 0;
            foreach (int i in idx)
            {
                double d = grid.Locations[i].Point.Eccentricity;
                double w = 1.0 / (cfg.WeightA * (d + cfg.WeightB));
                double p = usePd ? pdP[i] : tdP[i];
                double score = p > cfg.SignificanceLevel ? 100.0 : 100.0 * (1 - Math.Abs(td[i]) / normal[i]);
                if (sens[i] < 0) score = 0;
                num += w * score;
                den += w;
            }
            return num / den;
        }
    }
}
