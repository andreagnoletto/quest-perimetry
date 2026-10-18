using System;
using Perimetry.Core.Config;
using Perimetry.Core.Luminance;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Strategies
{
    /// <summary>Builds per-location procedures from config. <c>expectedDeviceDb</c> is the (possibly neighbour-shifted) expected threshold.</summary>
    public sealed class ProcedureFactory
    {
        private readonly StrategiesConfig _cfg;
        private readonly DbScale _scale;
        public StrategyKind Kind { get; }
        public bool IsZest => Kind == StrategyKind.Zest || Kind == StrategyKind.ZestFast;
        private ZestConfig Zest => Kind == StrategyKind.ZestFast ? _cfg.ZestFast : _cfg.Zest;
        private double[] _zestDomain;
        private double[,] _zestLikelihood;
        /// <summary>Lower asymptote of the ZEST likelihood: the configured false-positive rate, or the chance level 1/N in forced choice.</summary>
        public double GuessRate { get; }

        /// <summary>
        /// <paramref name="guessRate"/> (phase 5, forced choice): replaces the false-positive rate as the lower asymptote of the
        /// ZEST likelihood (a patient who did not see the stimulus is right 1/N of the time). Null = yes/no, OPI default.
        /// </summary>
        public ProcedureFactory(StrategyKind kind, StrategiesConfig cfg, DbScale scale, double? guessRate = null)
        {
            Kind = kind; _cfg = cfg; _scale = scale;
            if (IsZest)
            {
                var z = Zest;
                GuessRate = guessRate ?? z.FalsePositiveRate;
                _zestDomain = ZestProcedure.MakeDomain(z.DomainMinDb, z.DomainMaxDb, z.DomainStepDb);
                Func<double, double> sd;
                if (string.Equals(z.LikelihoodSd.Type, "henson", StringComparison.OrdinalIgnoreCase))
                {
                    var l = z.LikelihoodSd;
                    // Henson et al. 2000 variability is defined on the HFA scale.
                    sd = t => Math.Min(l.HensonCap, Math.Exp(l.HensonA * scale.ToHfaEquivalent(t) + l.HensonB));
                }
                else
                {
                    double fixedSd = z.LikelihoodSd.Sd;
                    sd = _ => fixedSd;
                }
                _zestLikelihood = ZestProcedure.GaussianLikelihood(_zestDomain, GuessRate, z.FalseNegativeRate, sd);
            }
        }

        /// <summary>
        /// Bimodal prior (Turpin et al. 2003 style): normal mode centred on the expected threshold and an
        /// abnormal ("glaucomatous") mode, mixed with the configured weights, plus a small floor so no
        /// threshold value has zero prior probability.
        /// </summary>
        public double[] ZestPrior(double expectedDeviceDb)
        {
            var p = Zest.Prior;
            double abnMean = _scale.FromHfaEquivalent(p.AbnormalMeanHfa);
            var prior = new double[_zestDomain.Length];
            double max = 0;
            for (int i = 0; i < prior.Length; i++)
            {
                double t = _zestDomain[i];
                prior[i] = p.NormalWeight * NormalDistribution.Pdf(t, expectedDeviceDb, p.NormalSdDb)
                         + p.AbnormalWeight * NormalDistribution.Pdf(t, abnMean, p.AbnormalSdDb);
                max = Math.Max(max, prior[i]);
            }
            for (int i = 0; i < prior.Length; i++) prior[i] += p.Floor * max;
            return prior;
        }

        public ILocationProcedure Create(double expectedDeviceDb)
        {
            double min = _cfg.MinStimulusDb, max = _cfg.MaxStimulusDb;
            switch (Kind)
            {
                case StrategyKind.Zest:
                case StrategyKind.ZestFast:
                    var z = Zest;
                    return new ZestProcedure(_zestDomain, ZestPrior(expectedDeviceDb), _zestLikelihood,
                        z.StopType, z.StopValue, min, max, z.MaxSeenLimit, z.MinNotSeenLimit, z.MaxPresentations, z.StimChoice);
                case StrategyKind.FullThreshold:
                    return new FullThresholdProcedure(Clamp(Math.Round(expectedDeviceDb), min, max), min, max,
                        _cfg.FullThreshold.RetestIfDiffersByDb, _cfg.FullThreshold.UseSecondStaircase);
                case StrategyKind.FourTwo:
                    return new FourTwoProcedure(Clamp(Math.Round(expectedDeviceDb), min, max), min, max);
                case StrategyKind.Screening:
                    double s = Clamp(Math.Round(expectedDeviceDb - _cfg.Screening.BrighterThanExpectedByDb), min, max);
                    return new ScreeningProcedure(s, _cfg.Screening.RetestNotSeen);
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private static double Clamp(double v, double lo, double hi) => Math.Max(lo, Math.Min(hi, v));
    }
}
