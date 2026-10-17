using System;
using System.Linq;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Strategies
{
    /// <summary>
    /// ZEST (King-Smith et al. 1994; Turpin et al. 2003) for one location.
    /// Direct port of OPI R/zest.r (Apache-2.0, A. Turpin): ZEST.start / ZEST.step / ZEST.stop /
    /// ZEST.final, with the same parameter names. The likelihood matrix is indexed
    /// [stimulus index, threshold index] exactly as OPI's.
    /// </summary>
    public sealed class ZestProcedure : ILocationProcedure
    {
        public string Name => "ZEST";

        public double[] Domain { get; }
        public double[] Pdf { get; private set; }
        private readonly double[,] _likelihood;
        public string StopType { get; }
        public double StopValue { get; }
        public double MinStimulus { get; }
        public double MaxStimulus { get; }
        public int MaxSeenLimit { get; }
        public int MinNotSeenLimit { get; }
        public int MaxPresentations { get; }
        public string StimChoice { get; }

        public int CurrSeenLimit { get; private set; }
        public int CurrNotSeenLimit { get; private set; }
        public int NumPresentations { get; private set; }

        public ZestProcedure(double[] domain, double[] prior, double[,] likelihood,
            string stopType = "S", double stopValue = 1.5,
            double? minStimulus = null, double? maxStimulus = null,
            int maxSeenLimit = 2, int minNotSeenLimit = 2, int maxPresentations = 100,
            string stimChoice = "mean")
        {
            if (stopType != "S" && stopType != "H" && stopType != "N")
                throw new ArgumentException("ZEST.start: stopType must be one of 'S', 'N', or 'H'");
            if (likelihood.GetLength(0) != domain.Length || likelihood.GetLength(1) != domain.Length)
                throw new ArgumentException("ZEST.start: likelihood must be length(domain) x length(domain)");
            if (prior.Length != domain.Length)
                throw new ArgumentException("ZEST.start: prior must have length(domain) entries");
            if (stimChoice != "mean" && stimChoice != "mode" && stimChoice != "median")
                throw new ArgumentException($"ZEST.step: stimChoice = {stimChoice} not implemented.");

            Domain = domain;
            MinStimulus = minStimulus ?? domain[0];
            MaxStimulus = maxStimulus ?? domain[domain.Length - 1];
            if (IndexOf(MinStimulus) < 0) throw new ArgumentException($"ZEST.start: minStimulus = {MinStimulus} is not in 'domain'.");
            if (IndexOf(MaxStimulus) < 0) throw new ArgumentException($"ZEST.start: maxStimulus = {MaxStimulus} is not in 'domain'.");

            double sum = prior.Sum();
            Pdf = prior.Select(p => p / sum).ToArray();
            _likelihood = likelihood;
            StopType = stopType;
            StopValue = stopValue;
            MaxSeenLimit = maxSeenLimit;
            MinNotSeenLimit = minNotSeenLimit;
            MaxPresentations = maxPresentations;
            StimChoice = stimChoice;
        }

        private int IndexOf(double v)
        {
            for (int i = 0; i < Domain.Length; i++) if (Math.Abs(Domain[i] - v) < 1e-9) return i;
            return -1;
        }

        /// <summary>OPI default likelihood: fpr + (1-fpr-fnr) * (1 - pnorm(stim, threshold, sd)).</summary>
        public static double[,] GaussianLikelihood(double[] domain, double fpr, double fnr, Func<double, double> sdForThreshold)
        {
            int n = domain.Length;
            var l = new double[n, n];
            for (int s = 0; s < n; s++)
                for (int t = 0; t < n; t++)
                    l[s, t] = fpr + (1 - fpr - fnr) * (1 - NormalDistribution.Cdf(domain[s], domain[t], sdForThreshold(domain[t])));
            return l;
        }

        public static double[] MakeDomain(double min, double max, double step)
        {
            int n = (int)Math.Round((max - min) / step) + 1;
            return Enumerable.Range(0, n).Select(i => Math.Round(min + i * step, 6)).ToArray();
        }

        public double Mean()
        {
            double m = 0;
            for (int i = 0; i < Domain.Length; i++) m += Pdf[i] * Domain[i];
            return m;
        }

        /// <summary>ZEST.stdev</summary>
        public double Stdev()
        {
            double m2 = 0, m = 0;
            for (int i = 0; i < Domain.Length; i++) { m2 += Pdf[i] * Domain[i] * Domain[i]; m += Pdf[i] * Domain[i]; }
            return Math.Sqrt(Math.Max(0, m2 - m * m));
        }

        /// <summary>ZEST.entropy (bits)</summary>
        public double Entropy()
        {
            double h = 0;
            foreach (var p in Pdf) if (p > 0) h -= p * Math.Log(p, 2);
            return h;
        }

        private int MedianIndex()
        {
            // which.min(abs(cumsum(pdf) - 0.5)) - first index on ties, as R's which.min.
            double c = 0, best = double.MaxValue; int bi = 0;
            for (int i = 0; i < Pdf.Length; i++)
            {
                c += Pdf[i];
                double d = Math.Abs(c - 0.5);
                if (d < best) { best = d; bi = i; }
            }
            return bi;
        }

        public double NextStimulusDb()
        {
            int idx;
            if (StimChoice == "mean")
            {
                double m = Mean(), best = double.MaxValue; idx = 0;
                for (int i = 0; i < Domain.Length; i++)
                {
                    double d = Math.Abs(Domain[i] - m);
                    if (d < best) { best = d; idx = i; }
                }
            }
            else if (StimChoice == "mode")
            {
                double best = double.MinValue; idx = 0;
                for (int i = 0; i < Pdf.Length; i++) if (Pdf[i] > best) { best = Pdf[i]; idx = i; }
            }
            else idx = MedianIndex();

            double stim = Domain[idx];
            if (stim < MinStimulus) stim = MinStimulus;
            if (stim > MaxStimulus) stim = MaxStimulus;
            return stim;
        }

        public void Update(double stimulusDb, bool seen)
        {
            int si = IndexOf(stimulusDb);
            if (si < 0) throw new ArgumentException($"ZEST.step: stimulus {stimulusDb} is not in 'domain'.");
            NumPresentations++;
            var pdf = Pdf;
            if (seen)
            {
                if (Math.Abs(stimulusDb - MaxStimulus) < 1e-9) CurrSeenLimit++;
                for (int t = 0; t < pdf.Length; t++) pdf[t] *= _likelihood[si, t];
            }
            else
            {
                if (Math.Abs(stimulusDb - MinStimulus) < 1e-9) CurrNotSeenLimit++;
                for (int t = 0; t < pdf.Length; t++) pdf[t] *= 1 - _likelihood[si, t];
            }
            double sum = pdf.Sum();
            for (int t = 0; t < pdf.Length; t++) pdf[t] /= sum;
        }

        public bool IsFinished
        {
            get
            {
                bool keepGoing =
                    NumPresentations < MaxPresentations &&
                    CurrNotSeenLimit < MinNotSeenLimit &&
                    CurrSeenLimit < MaxSeenLimit &&
                    ((StopType == "S" && Stdev() > StopValue) ||
                     (StopType == "H" && Entropy() > StopValue) ||
                     (StopType == "N" && NumPresentations < StopValue));
                return !keepGoing;
            }
        }

        public double FinalDb
        {
            get
            {
                if (StimChoice == "mean") return Mean();
                if (StimChoice == "mode")
                {
                    double best = double.MinValue; int idx = 0;
                    for (int i = 0; i < Pdf.Length; i++) if (Pdf[i] > best) { best = Pdf[i]; idx = i; }
                    return Domain[idx];
                }
                return Domain[MedianIndex()];
            }
        }

        public string StopReason
        {
            get
            {
                if (!IsFinished) return "Not";
                if (CurrNotSeenLimit >= MinNotSeenLimit) return "Min";
                if (CurrSeenLimit >= MaxSeenLimit) return "Max";
                if (NumPresentations >= MaxPresentations) return "MaxPresentations";
                return "Stop";
            }
        }

        public bool IsCensoredLow => CurrNotSeenLimit >= MinNotSeenLimit || FinalDb < MinStimulus;
        public bool IsCensoredHigh => CurrSeenLimit >= MaxSeenLimit;
    }
}
