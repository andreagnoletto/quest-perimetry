using System;
using System.Collections.Generic;
using System.Linq;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Exam
{
    /// <summary>
    /// The exam "server" (OPI client/server style): decides every presentation and interprets every
    /// response, independent of how stimuli are rendered. Usage: loop { req = Next(); show it; Submit(result); }
    /// until <see cref="IsFinished"/>. Everything is logged in <see cref="BuildRecord"/>.
    /// Deterministic for a given seed and response sequence.
    /// Order of an exam: optional foveal threshold (diamond target) → blind-spot search (Heijl-Krakau
    /// modes) → threshold test with interleaved catch trials.
    /// </summary>
    public sealed class ExamSession
    {
        public const string Version = "0.3.0-phase5";

        private readonly ExamSettings _s;
        private readonly ClinicalConfig _cfg;
        private readonly INormativeDatabase _norm;
        private readonly DbScale _scale;
        private readonly Rng _rng;
        private readonly TestGrid _grid;
        private readonly ThresholdStrategy _strategy;
        private BlindSpotLocator _bsLocator;
        private readonly ExamRecord _record;
        private readonly double _sizeDeg;

        // Phase 5
        private readonly bool _forcedChoice;
        private readonly bool _oculokinetic;
        private readonly bool _gazeRejects;
        private readonly ILocationProcedure _foveal;
        private readonly double _fovealStart;
        private readonly string _fovealStrategy;
        private double _gazeErrorSum;

        private PresentationRequest _pending;
        private int _seq;
        private int _toFp, _toFn, _toFl;
        private bool _hkActive;
        private int _consecutiveFixationLosses;
        private FieldPoint? _blindSpot;

        public TestGrid Grid => _grid;
        public ThresholdStrategy Strategy => _strategy;
        /// <summary>Procedure of the optional foveal threshold (null when off).</summary>
        public ILocationProcedure FovealProcedure => _foveal;

        public ExamSession(ExamSettings settings, ClinicalConfig cfg, INormativeDatabase normative, DbScale scale, string calibrationRef = null)
        {
            _s = settings; _cfg = cfg; _norm = normative; _scale = scale;
            _rng = new Rng(settings.Seed);
            _grid = GridLibrary.Get(cfg, settings.GridId);
            if (normative.GridId != _grid.Id) throw new ArgumentException($"normative is for grid {normative.GridId}, exam uses {_grid.Id}");
            _sizeDeg = Stimulus.DiameterDeg(cfg.Stimulus, settings.StimulusSize);

            _forcedChoice = settings.ResponseMode == ResponseMode.ForcedChoice;
            if (_forcedChoice && cfg.Strategies.ForcedChoice.Intervals < 2) throw new ArgumentException("forcedChoice.intervals must be >= 2");
            _oculokinetic = settings.FixationMode == FixationMode.Oculokinetic;
            _gazeRejects = cfg.Reliability.Gaze.RetestRejected &&
                           (settings.FixationMode == FixationMode.CentralEyeTracking || _oculokinetic);
            double? guess = _forcedChoice && cfg.Strategies.ForcedChoice.ZestGuessRate ? 1.0 / cfg.Strategies.ForcedChoice.Intervals : (double?)null;

            var expected = _grid.Locations.Select(l => scale.FromHfaEquivalent(normative.ExpectedHfa(l.Index, settings.AgeYears))).ToList();
            var factory = new ProcedureFactory(settings.Strategy, cfg.Strategies, scale, guess);
            _strategy = new ThresholdStrategy(_grid, factory, expected, cfg.Strategies.Growth.UseNeighbourSeeding, _rng.Fork());

            if (settings.FovealThreshold)
            {
                var kind = FovealStrategyKind(settings.Strategy, cfg.Strategies.Foveal.Strategy);
                _fovealStrategy = kind.ToString();
                var ff = kind == settings.Strategy ? factory : new ProcedureFactory(kind, cfg.Strategies, scale, guess);
                double expHfa = normative.ExpectedFovealHfa(settings.AgeYears);
                if (double.IsNaN(expHfa))
                {
                    // No foveal normative: start from the expected value of the grid point closest to the centre.
                    var nearest = _grid.Locations.OrderBy(l => l.Point.Eccentricity).First();
                    expHfa = normative.ExpectedHfa(nearest.Index, settings.AgeYears);
                }
                _fovealStart = scale.FromHfaEquivalent(expHfa);
                _foveal = ff.Create(_fovealStart);
            }

            // Heijl-Krakau needs a "no response" to mean "not seen"; in forced choice a guess is right 1/N of the
            // time, so neither the blind-spot search nor the catch trials are meaningful: fixation is then
            // monitored only by eye tracking (see FieldAnalysis warning).
            bool heijlKrakau = HeijlKrakauActive(settings, cfg);
            if (heijlKrakau) _bsLocator = new BlindSpotLocator(cfg.Reliability.BlindSpot);

            var r = cfg.Reliability;
            _toFp = Draw(r.FalsePositive);
            _toFn = Draw(r.FalseNegative);
            _toFl = Draw(r.FixationLoss);

            _record = new ExamRecord
            {
                SoftwareVersion = Version,
                ConfigHash = cfg.Hash,
                ExamId = $"{settings.Eye}-{settings.GridId}-{settings.Strategy}-{settings.Seed}",
                StartedUtc = DateTime.UtcNow,
                Settings = settings,
                Calibrated = scale.IsCalibrated,
                CalibrationRef = calibrationRef ?? "nominal-sRGB",
                BackgroundCdm2 = scale.BackgroundCdm2,
                MaxLuminanceCdm2 = scale.MaxLuminanceCdm2,
                HfaOffsetDb = scale.HfaOffsetDb,
                NormativeId = normative.Id,
                NormativeProvisional = normative.IsProvisional,
            };
            _record.Reliability.FalsePositives.AlertLimit = r.FalsePositive.AlertLimit;
            _record.Reliability.FalseNegatives.AlertLimit = r.FalseNegative.AlertLimit;
            _record.Reliability.FixationLosses.AlertLimit = r.FixationLoss.AlertLimit;
        }

        /// <summary>Whether Heijl-Krakau blind-spot monitoring runs for these settings (modes CentralHeijlKrakau and Oculokinetic, never in forced choice).</summary>
        public static bool HeijlKrakauActive(ExamSettings settings, ClinicalConfig cfg) =>
            settings.ResponseMode != ResponseMode.ForcedChoice &&
            (settings.FixationMode == FixationMode.CentralHeijlKrakau
             || (settings.FixationMode == FixationMode.Oculokinetic && cfg.Reliability.Oculokinetic.HeijlKrakau));

        /// <summary>"exam" = the exam's strategy (ZEST when the exam is a screening, which has no threshold procedure).</summary>
        public static StrategyKind FovealStrategyKind(StrategyKind examStrategy, string configured)
        {
            if (string.IsNullOrEmpty(configured) || string.Equals(configured, "exam", StringComparison.OrdinalIgnoreCase))
                return examStrategy == StrategyKind.Screening ? StrategyKind.Zest : examStrategy;
            var k = (StrategyKind)Enum.Parse(typeof(StrategyKind), configured, true);
            if (k == StrategyKind.Screening) throw new ArgumentException("foveal.strategy cannot be Screening");
            return k;
        }

        private int Draw(CatchTrialConfig c) => Math.Max(1, c.EveryN + _rng.NextInt(-c.Jitter, c.Jitter + 1));

        public bool IsFinished => _pending == null && (_foveal == null || _foveal.IsFinished)
                                  && (_bsLocator == null || _bsLocator.IsFinished) && _strategy.IsFinished;

        public PresentationRequest Next()
        {
            if (_pending != null) throw new InvalidOperationException("Submit the pending presentation first");
            if (IsFinished) return null;

            if (_foveal != null && !_foveal.IsFinished)
                return Issue(PresentationKind.Foveal, -1, new FieldPoint(0, 0), _foveal.NextStimulusDb());

            if (_bsLocator != null && !_bsLocator.IsFinished)
                return Issue(PresentationKind.BlindSpotMapping, -1, _bsLocator.Current, _bsLocator.StimulusDb);

            var rel = _cfg.Reliability;
            // Forced choice has no "no stimulus" trial: the patient must always pick an interval.
            if (_s.FalsePositiveCatchTrials && !_forcedChoice && _toFp <= 0)
            {
                _toFp = Draw(rel.FalsePositive);
                return Issue(PresentationKind.FalsePositiveCatch, -1, new FieldPoint(0, 0), double.NaN);
            }
            if (_hkActive && _toFl <= 0)
            {
                _toFl = Draw(rel.FixationLoss);
                return Issue(PresentationKind.BlindSpotCatch, -1, _blindSpot.Value, rel.BlindSpot.StimulusDb);
            }
            if (_s.FalseNegativeCatchTrials && _toFn <= 0)
            {
                var eligible = _grid.Locations.Where(l => !l.IsBlindSpot
                        && _strategy.StateOf(l.Index) == ThresholdStrategy.LocationState.Finished
                        && !double.IsNaN(_strategy.ProcedureOf(l.Index).FinalDb)
                        && _strategy.ProcedureOf(l.Index).FinalDb - rel.FalseNegative.BrighterByDb >= _cfg.Strategies.MinStimulusDb)
                    .ToList();
                if (eligible.Count > 0)
                {
                    _toFn = Draw(rel.FalseNegative);
                    var loc = eligible[_rng.NextInt(0, eligible.Count)];
                    double db = Math.Round(_strategy.ProcedureOf(loc.Index).FinalDb - rel.FalseNegative.BrighterByDb);
                    return Issue(PresentationKind.FalseNegativeCatch, loc.Index, loc.Point, db);
                }
            }

            int? next = _strategy.PickNextLocation();
            if (next == null) return null;
            var l2 = _grid.Locations[next.Value];
            return Issue(PresentationKind.Threshold, l2.Index, l2.Point, _strategy.NextStimulusDb(l2.Index));
        }

        private PresentationRequest Issue(PresentationKind kind, int loc, FieldPoint odPoint, double db)
        {
            bool shown = kind != PresentationKind.FalsePositiveCatch;
            var p = odPoint.ForEye(_s.Eye);
            var t = _cfg.Timing;
            var fc = _cfg.Strategies.ForcedChoice;
            // The foveal stimulus is shown with the central target still: the diamond's centre is the test point.
            var target = _oculokinetic && kind != PresentationKind.Foveal ? DrawTarget(p) : new FieldPoint(0, 0);
            _pending = new PresentationRequest
            {
                Sequence = ++_seq,
                Kind = kind,
                LocationIndex = loc,
                FieldX = p.X,
                FieldY = p.Y,
                StimulusShown = shown,
                Db = shown ? db : double.NaN,
                IncrementCdm2 = shown ? _scale.DbToIncrement(db) : 0,
                LuminanceCdm2 = shown ? _scale.DbToStimulusLuminance(db) : _scale.BackgroundCdm2,
                Size = _s.StimulusSize,
                SizeDeg = _sizeDeg,
                DurationMs = _forcedChoice ? fc.IntervalMs : _cfg.Stimulus.DurationMs,
                ResponseWindowMs = t.ResponseWindowMs,
                PreStimulusDelayMs = _rng.NextInt(t.IsiMinMs, t.IsiMaxMs + 1),
                FixationTarget = kind == PresentationKind.Foveal && string.Equals(_cfg.Strategies.Foveal.FixationTarget, "Diamond4", StringComparison.OrdinalIgnoreCase)
                    ? FixationTargetKind.Diamond4 : FixationTargetKind.Central,
                TargetX = target.X,
                TargetY = target.Y,
            };
            if (_forcedChoice)
            {
                _pending.Intervals = fc.Intervals;
                _pending.StimulusInterval = shown ? _rng.NextInt(1, fc.Intervals + 1) : 0;
                _pending.IntervalMs = fc.IntervalMs;
                _pending.IntervalGapMs = fc.GapMs;
                _pending.MarkerMs = fc.MarkerMs;
            }
            return _pending;
        }

        /// <summary>
        /// Oculokinetic target: uniform in ±targetRangeDeg on both axes, redrawn while the composed stimulus
        /// direction would exceed maxEccentricityDeg; straight ahead if no draw fits.
        /// </summary>
        private FieldPoint DrawTarget(FieldPoint relativeEyePoint)
        {
            var o = _cfg.Reliability.Oculokinetic;
            for (int i = 0; i < 20; i++)
            {
                var target = new FieldPoint(_rng.Uniform(-o.TargetRangeDeg, o.TargetRangeDeg), _rng.Uniform(-o.TargetRangeDeg, o.TargetRangeDeg));
                var abs = AngularGeometry.Compose(target, relativeEyePoint);
                if (AngularGeometry.Eccentricity(abs) <= o.MaxEccentricityDeg) return target;
            }
            return new FieldPoint(0, 0);
        }

        /// <summary>Total time of the stimulus part of a presentation (0 for yes/no; all intervals and gaps in forced choice).</summary>
        public static double StimulusSpanMs(PresentationRequest req) =>
            req.Intervals > 0 ? req.Intervals * req.IntervalMs + (req.Intervals - 1) * req.IntervalGapMs : 0;

        public void Submit(PresentationResult result)
        {
            if (_pending == null || result.Sequence != _pending.Sequence)
                throw new InvalidOperationException("result does not match the pending presentation");
            var req = _pending;
            _pending = null;

            var t = _cfg.Timing;
            if (result.Discarded)
            {
                // Not shown to completion: nothing is learned, the same kind of trial will be issued again.
                _record.Reliability.AbortedPresentations++;
                _record.Presentations.Add(new PresentationLogEntry { Request = req, Result = result, Disposition = "aborted" });
                if (req.Kind == PresentationKind.FalsePositiveCatch) _toFp = 0;
                else if (req.Kind == PresentationKind.FalseNegativeCatch) _toFn = 0;
                else if (req.Kind == PresentationKind.BlindSpotCatch) _toFl = 0;
                return;
            }

            bool anticipated, seen, responded;
            if (req.Intervals > 0)
            {
                // Forced choice: the answer is an interval; correct interval = seen. There is no anticipation rule,
                // the choice is made after the last interval.
                responded = result.RespondedInterval.HasValue;
                seen = responded && result.RespondedInterval.Value == req.StimulusInterval;
                anticipated = false;
            }
            else
            {
                anticipated = result.Responded && result.LatencyMs.HasValue && result.LatencyMs.Value < t.MinValidLatencyMs;
                bool inWindow = result.Responded && (!result.LatencyMs.HasValue || result.LatencyMs.Value <= t.ResponseWindowMs);
                responded = result.Responded;
                seen = inWindow && !anticipated;
            }
            var rel = _record.Reliability;
            string disposition;

            switch (req.Kind)
            {
                case PresentationKind.Foveal:
                    if (anticipated) { rel.AnticipatedResponses++; disposition = "anticipated"; }
                    else if (GazeRejected(req, result, out _)) { rel.GazeRejectedPresentations++; disposition = "gazeRejected"; }
                    else { _foveal.Update(req.Db, seen); disposition = "foveal"; }
                    break;
                case PresentationKind.BlindSpotMapping:
                    _bsLocator.Update(seen);
                    disposition = rel.BlindSpotRemaps > 0 ? "remapping" : "mapping";
                    if (_bsLocator.IsFinished)
                    {
                        _blindSpot = _bsLocator.Found;
                        _hkActive = _blindSpot.HasValue;
                        rel.BlindSpotFound = _hkActive;
                        if (_hkActive) { var bs = _blindSpot.Value.ForEye(_s.Eye); rel.BlindSpotX = bs.X; rel.BlindSpotY = bs.Y; }
                    }
                    break;
                case PresentationKind.FalsePositiveCatch:
                    rel.FalsePositives.Trials++;
                    if (responded && !anticipated) rel.FalsePositives.Errors++;
                    disposition = "catch";
                    break;
                case PresentationKind.FalseNegativeCatch:
                    rel.FalseNegatives.Trials++;
                    if (!seen) rel.FalseNegatives.Errors++;
                    disposition = "catch";
                    break;
                case PresentationKind.BlindSpotCatch:
                    rel.FixationLosses.Trials++;
                    if (seen) rel.FixationLosses.Errors++;
                    disposition = "catch";
                    _consecutiveFixationLosses = seen ? _consecutiveFixationLosses + 1 : 0;
                    int remapAfter = _cfg.Reliability.BlindSpot.RemapAfterConsecutiveLosses;
                    if (remapAfter > 0 && _consecutiveFixationLosses >= remapAfter)
                    {
                        // The catch point may sit on the edge of the blind spot (seen with normal latency while
                        // fixation is fine): search it again before counting more losses there.
                        _consecutiveFixationLosses = 0;
                        rel.BlindSpotRemaps++;
                        _hkActive = false;
                        _bsLocator = new BlindSpotLocator(_cfg.Reliability.BlindSpot);
                    }
                    break;
                default:
                    bool rejectedByGaze = GazeRejected(req, result, out double? gazeErr);
                    if (result.GazeTracked && gazeErr.HasValue) RecordGaze(req, result, gazeErr.Value, rejectedByGaze || !result.FixationOk);
                    if (anticipated) { rel.AnticipatedResponses++; disposition = "anticipated"; }
                    else if (!result.FixationOk) { rel.GazeRejectedPresentations++; disposition = "fixationLost"; }
                    else if (rejectedByGaze) { rel.GazeRejectedPresentations++; disposition = "gazeRejected"; }
                    else
                    {
                        _strategy.Update(req.LocationIndex, req.Db, seen);
                        disposition = "used";
                    }
                    _toFp--; _toFn--; _toFl--;
                    break;
            }

            rel.TotalPresentations++;
            double end = result.OnsetMs + StimulusSpanMs(req) + (seen && result.LatencyMs.HasValue ? result.LatencyMs.Value : t.ResponseWindowMs);
            rel.DurationMs = Math.Max(rel.DurationMs, end);
            _record.Presentations.Add(new PresentationLogEntry { Request = req, Result = result, Disposition = disposition });
        }

        /// <summary>Eye-tracking rule (phase 5): gaze farther than reliability.gaze.maxErrorDeg from the target at onset rejects the presentation.</summary>
        private bool GazeRejected(PresentationRequest req, PresentationResult result, out double? error)
        {
            error = result.GazeTracked ? (result.GazeErrorDeg ?? result.GazeMaxErrorDeg) : null;
            return _gazeRejects && error.HasValue && error.Value > _cfg.Reliability.Gaze.MaxErrorDeg;
        }

        private void RecordGaze(PresentationRequest req, PresentationResult result, double error, bool rejected)
        {
            var rel = _record.Reliability;
            rel.GazeTracked = true;
            rel.GazeSamples++;
            _gazeErrorSum += error;
            rel.GazeMeanErrorDeg = _gazeErrorSum / rel.GazeSamples;
            if (_cfg.Reliability.Gaze.RecordTrace)
                rel.GazeTrace.Add(new GazeSample { Sequence = req.Sequence, OnsetMs = result.OnsetMs, ErrorDeg = error, Rejected = rejected });
        }

        /// <summary>Records a pause (no presentation may be pending). Times are ms since exam start.</summary>
        public void RecordPause(double startMs, double endMs, string reason)
        {
            if (_pending != null) throw new InvalidOperationException("Submit or discard the pending presentation first");
            if (endMs < startMs) throw new ArgumentException("endMs < startMs");
            _record.Reliability.Pauses.Add(new PauseInterval { StartMs = startMs, EndMs = endMs, Reason = reason });
        }

        public ExamRecord BuildRecord()
        {
            _record.Locations = _grid.Locations.Select(l =>
            {
                var p = _strategy.ProcedureOf(l.Index);
                return new LocationResult
                {
                    Index = l.Index,
                    X = l.Point.X,
                    Y = l.Point.Y,
                    IsBlindSpot = l.IsBlindSpot,
                    ThresholdDb = p?.FinalDb ?? double.NaN,
                    CensoredLow = p?.IsCensoredLow ?? false,
                    CensoredHigh = p?.IsCensoredHigh ?? false,
                    StopReason = p?.StopReason ?? "NotStarted",
                    Presentations = p?.NumPresentations ?? 0,
                    StartValueDb = _strategy.StartValueOf(l.Index),
                    ScreeningOutcome = (p as ScreeningProcedure)?.Outcome.ToString(),
                };
            }).ToList();
            if (_foveal != null)
                _record.Foveal = new FovealResult
                {
                    ThresholdDb = _foveal.FinalDb,
                    CensoredLow = _foveal.IsCensoredLow,
                    CensoredHigh = _foveal.IsCensoredHigh,
                    StopReason = _foveal.StopReason,
                    Presentations = _foveal.NumPresentations,
                    StartValueDb = _fovealStart,
                    Strategy = _fovealStrategy,
                };
            return _record;
        }
    }
}
