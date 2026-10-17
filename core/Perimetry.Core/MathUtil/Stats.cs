using System;
using System.Collections.Generic;
using System.Linq;

namespace Perimetry.Core.MathUtil
{
    public static class Stats
    {
        public static double Mean(IReadOnlyList<double> v) => v.Count == 0 ? double.NaN : v.Sum() / v.Count;

        public static double Sd(IReadOnlyList<double> v)
        {
            if (v.Count < 2) return double.NaN;
            double m = Mean(v);
            return Math.Sqrt(v.Sum(x => (x - m) * (x - m)) / (v.Count - 1));
        }

        public static double WeightedMean(IReadOnlyList<double> v, IReadOnlyList<double> w)
        {
            double sw = 0, s = 0;
            for (int i = 0; i < v.Count; i++) { s += v[i] * w[i]; sw += w[i]; }
            return s / sw;
        }

        /// <summary>Sample quantile, R type 7 (the R default), on an unsorted list.</summary>
        public static double Quantile(IReadOnlyList<double> values, double p)
        {
            if (values.Count == 0) return double.NaN;
            var sorted = values.OrderBy(x => x).ToArray();
            return QuantileSorted(sorted, p);
        }

        public static double QuantileSorted(double[] sorted, double p)
        {
            int n = sorted.Length;
            if (n == 1) return sorted[0];
            double h = (n - 1) * p;
            int lo = (int)Math.Floor(h);
            int hi = Math.Min(lo + 1, n - 1);
            return sorted[lo] + (h - lo) * (sorted[hi] - sorted[lo]);
        }

        /// <summary>Ordinary least squares y = a + b x. Returns (intercept, slope).</summary>
        public static (double intercept, double slope) LinearFit(IReadOnlyList<double> x, IReadOnlyList<double> y)
        {
            int n = x.Count;
            if (n < 2) throw new ArgumentException("need at least two points");
            double mx = Mean(x), my = Mean(y), sxx = 0, sxy = 0;
            for (int i = 0; i < n; i++) { sxx += (x[i] - mx) * (x[i] - mx); sxy += (x[i] - mx) * (y[i] - my); }
            double b = sxx == 0 ? 0 : sxy / sxx;
            return (my - b * mx, b);
        }
    }
}
