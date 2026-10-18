using System;
using System.Linq;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Simulation
{
    public enum PsychometricModel
    {
        /// <summary>OPI SimHenson: cumulative Gaussian with sd = min(cap, exp(A*t + B)) (Henson et al. 2000).</summary>
        Henson,
        /// <summary>OPI SimGaussian: cumulative Gaussian with constant sd.</summary>
        Gaussian,
    }

    public sealed class ObserverParameters
    {
        public PsychometricModel Model { get; set; } = PsychometricModel.Henson;
        /// <summary>OPI SimHenson type "C" (combined) defaults.</summary>
        public double HensonA { get; set; } = -0.081;
        public double HensonB { get; set; } = 3.27;
        public double HensonCap { get; set; } = 6;
        public double GaussianSd { get; set; } = 1;
        public double FalsePositiveRate { get; set; } = 0.03;
        public double FalseNegativeRate { get; set; } = 0.01;
        /// <summary>Probability that fixation is off target during a presentation.</summary>
        public double FixationLossRate { get; set; } = 0.0;
        /// <summary>
        /// Simulated eye tracker (phase 5): 0 = no tracker. Otherwise each presentation reports gaze error =
        /// |N(0, sd)²D| when fixating, or a saccade of <see cref="GazeLossMinDeg"/>..<see cref="GazeLossMaxDeg"/> when fixation is lost.
        /// </summary>
        public double GazeNoiseSdDeg { get; set; } = 0.0;
        public double GazeLossMinDeg { get; set; } = 4;
        public double GazeLossMaxDeg { get; set; } = 10;
        /// <summary>Forced choice: the simulated patient picks a random interval when the stimulus was not seen (chance level 1/N).</summary>
        public bool ForcedChoiceGuesses { get; set; } = true;
        public double MeanLatencyMs { get; set; } = 450;
        public double LatencySdMs { get; set; } = 90;
        /// <summary>Physiological blind spot (OD convention): centre and semi-axes in degrees.</summary>
        public double BlindSpotX { get; set; } = 15;
        public double BlindSpotY { get; set; } = -1.5;
        public double BlindSpotRadiusX { get; set; } = 2.75;
        public double BlindSpotRadiusY { get; set; } = 3.75;
    }

    /// <summary>
    /// Simulated patient. The probability of seeing a stimulus of s dB (HFA-equivalent) at a location
    /// with true threshold t is, as OPI's simulators: fpr + (1 - fpr - fnr) * (1 - pnorm(s, t, sd)).
    /// Inside the physiological blind spot only false positives occur. When fixation is lost the
    /// stimulus falls on seeing retina and is seen with probability 1 - fnr.
    /// </summary>
    public sealed class SimulatedObserver
    {
        private readonly SyntheticField _field;
        private readonly DbScale _scale;
        private readonly Eye _eye;
        private readonly Rng _rng;
        public ObserverParameters P { get; }

        public SimulatedObserver(SyntheticField field, DbScale scale, Eye eye, ObserverParameters p, Rng rng)
        {
            _field = field; _scale = scale; _eye = eye; P = p; _rng = rng;
        }

        public double SdAt(double thresholdHfa) => P.Model == PsychometricModel.Henson
            ? Math.Min(P.HensonCap, Math.Exp(P.HensonA * thresholdHfa + P.HensonB))
            : P.GaussianSd;

        public bool InBlindSpot(FieldPoint odPoint)
        {
            double dx = (odPoint.X - P.BlindSpotX) / P.BlindSpotRadiusX, dy = (odPoint.Y - P.BlindSpotY) / P.BlindSpotRadiusY;
            return dx * dx + dy * dy <= 1;
        }

        /// <summary>True threshold at a position (OD convention): the grid value, or the nearest grid point's.</summary>
        public double TrueThresholdAt(FieldPoint odPoint)
        {
            if (InBlindSpot(odPoint)) return double.NegativeInfinity;
            var g = _field.Grid;
            int i = g.IndexOf(odPoint) ?? g.Locations.OrderBy(l => l.Point.DistanceTo(odPoint)).First().Index;
            return _field.TrueHfaDb[i];
        }

        public double ProbabilitySeen(double stimulusDeviceDb, FieldPoint odPoint)
        {
            double t = TrueThresholdAt(odPoint);
            if (double.IsNegativeInfinity(t)) return P.FalsePositiveRate;
            double s = _scale.ToHfaEquivalent(stimulusDeviceDb);
            return P.FalsePositiveRate + (1 - P.FalsePositiveRate - P.FalseNegativeRate) * (1 - NormalDistribution.Cdf(s, t, SdAt(t)));
        }

        public PresentationResult Respond(PresentationRequest req, double onsetMs)
        {
            // FieldX/FieldY are retinal offsets from the target (also in oculokinetic mode), so the field model applies directly.
            var od = new FieldPoint(req.FieldX, req.FieldY).ForEye(_eye); // ForEye is its own inverse
            bool seen;
            bool fixationLost = req.StimulusShown && _rng.Bernoulli(P.FixationLossRate);
            if (!req.StimulusShown) seen = _rng.Bernoulli(P.FalsePositiveRate);
            else if (fixationLost) seen = _rng.Bernoulli(1 - P.FalseNegativeRate);
            else seen = _rng.Bernoulli(ProbabilitySeen(req.Db, od));

            var res = new PresentationResult { Sequence = req.Sequence, OnsetMs = onsetMs, FixationOk = true };
            if (P.GazeNoiseSdDeg > 0)
            {
                res.GazeTracked = true;
                double gx = _rng.Gaussian(0, P.GazeNoiseSdDeg), gy = _rng.Gaussian(0, P.GazeNoiseSdDeg);
                double err = Math.Sqrt(gx * gx + gy * gy);
                if (fixationLost) err = _rng.Uniform(P.GazeLossMinDeg, P.GazeLossMaxDeg);
                res.GazeErrorDeg = err;
                res.GazeMaxErrorDeg = err;
            }

            if (req.Intervals > 0)
            {
                // Forced choice: correct interval when seen, otherwise a guess (or no answer when guessing is off).
                int? choice = seen ? req.StimulusInterval
                    : P.ForcedChoiceGuesses ? _rng.NextInt(1, req.Intervals + 1) : (int?)null;
                res.RespondedInterval = choice;
                res.Responded = choice.HasValue;
                res.LatencyMs = choice.HasValue ? Math.Max(150, Math.Min(req.ResponseWindowMs, _rng.Gaussian(P.MeanLatencyMs, P.LatencySdMs))) : (double?)null;
                return res;
            }

            double? latency = null;
            if (seen)
                latency = req.StimulusShown
                    ? Math.Max(150, Math.Min(req.ResponseWindowMs, _rng.Gaussian(P.MeanLatencyMs, P.LatencySdMs)))
                    : _rng.Uniform(150, req.ResponseWindowMs);
            res.Responded = seen;
            res.LatencyMs = latency;
            return res;
        }
    }
}
