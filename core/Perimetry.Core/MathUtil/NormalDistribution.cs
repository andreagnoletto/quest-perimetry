using System;

namespace Perimetry.Core.MathUtil
{
    /// <summary>Normal distribution functions with double precision accuracy.</summary>
    public static class NormalDistribution
    {
        /// <summary>
        /// Standard normal CDF, Hart (1968) algorithm 5666 as given by West (2005),
        /// "Better approximations to cumulative normal functions". Accurate to ~1e-15,
        /// which keeps ZEST likelihoods numerically identical to R's pnorm for cross-checks.
        /// </summary>
        public static double StandardCdf(double x)
        {
            double xAbs = Math.Abs(x);
            double c;
            if (xAbs > 37.0)
            {
                c = 0.0;
            }
            else
            {
                double e = Math.Exp(-xAbs * xAbs / 2.0);
                if (xAbs < 7.07106781186547)
                {
                    double b = 3.52624965998911E-02 * xAbs + 0.700383064443688;
                    b = b * xAbs + 6.37396220353165;
                    b = b * xAbs + 33.912866078383;
                    b = b * xAbs + 112.079291497871;
                    b = b * xAbs + 221.213596169931;
                    b = b * xAbs + 220.206867912376;
                    c = e * b;
                    b = 8.83883476483184E-02 * xAbs + 1.75566716318264;
                    b = b * xAbs + 16.064177579207;
                    b = b * xAbs + 86.7807322029461;
                    b = b * xAbs + 296.564248779674;
                    b = b * xAbs + 637.333633378831;
                    b = b * xAbs + 793.826512519948;
                    b = b * xAbs + 440.413735824752;
                    c /= b;
                }
                else
                {
                    double b = xAbs + 0.65;
                    b = xAbs + 4.0 / b;
                    b = xAbs + 3.0 / b;
                    b = xAbs + 2.0 / b;
                    b = xAbs + 1.0 / b;
                    c = e / b / 2.506628274631;
                }
            }
            return x > 0 ? 1.0 - c : c;
        }

        /// <summary>Equivalent of R's pnorm(x, mean, sd).</summary>
        public static double Cdf(double x, double mean, double sd) => StandardCdf((x - mean) / sd);

        public static double Pdf(double x, double mean, double sd)
        {
            double z = (x - mean) / sd;
            return Math.Exp(-0.5 * z * z) / (sd * Math.Sqrt(2.0 * Math.PI));
        }

        private static readonly double[] A = { -3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00 };
        private static readonly double[] B = { -5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01 };
        private static readonly double[] C = { -7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00 };
        private static readonly double[] D = { 7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00 };

        /// <summary>Standard normal quantile (Acklam's algorithm + one Halley refinement step).</summary>
        public static double StandardQuantile(double p)
        {
            if (p <= 0.0) return double.NegativeInfinity;
            if (p >= 1.0) return double.PositiveInfinity;
            const double pLow = 0.02425;
            double x;
            if (p < pLow)
            {
                double q = Math.Sqrt(-2 * Math.Log(p));
                x = (((((C[0] * q + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5]) /
                    ((((D[0] * q + D[1]) * q + D[2]) * q + D[3]) * q + 1);
            }
            else if (p <= 1 - pLow)
            {
                double q = p - 0.5, r = q * q;
                x = (((((A[0] * r + A[1]) * r + A[2]) * r + A[3]) * r + A[4]) * r + A[5]) * q /
                    (((((B[0] * r + B[1]) * r + B[2]) * r + B[3]) * r + B[4]) * r + 1);
            }
            else
            {
                double q = Math.Sqrt(-2 * Math.Log(1 - p));
                x = -(((((C[0] * q + C[1]) * q + C[2]) * q + C[3]) * q + C[4]) * q + C[5]) /
                     ((((D[0] * q + D[1]) * q + D[2]) * q + D[3]) * q + 1);
            }
            double e = StandardCdf(x) - p;
            double u = e * Math.Sqrt(2 * Math.PI) * Math.Exp(x * x / 2);
            return x - u / (1 + x * u / 2);
        }
    }
}
