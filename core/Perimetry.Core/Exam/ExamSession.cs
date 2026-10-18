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
    /// </summary>
    public sealed class ExamSession
    {
        public const string Version = "0.2.0-phase2";

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

        private PresentationRequest _pending;
        private int _seq;
        private int _toFp, _toFn, _toFl;
        private bool _hkActive;
        private int _consecutiveFixationLosses;
        private FieldPoint? _blindSpot;

        public TestGrid Grid => _grid;
        public ThresholdStrategy Strategy => _strategy;

        public ExamSession(ExamSettings settings, ClinicalConfig cfg, INormativeDatabase normative, DbScale scale, string calibrationRef = null)
        {
            _s = settings; _cfg = cfg; _norm = normative; _scale = scale;
            _rng = new Rng(settings.Seed);
            _grid = GridLibrary.Get(cfg, settings.GridId);
            if (normative.GridId != _grid.Id) throw new ArgumentException($"normative is for grid {normative.GridId}, exam uses {_grid.Id}");
            _sizeDeg = Stimulus.DiameterDeg(cfg.Stimulus, settings.StimulusSize);

            var expected = _grid.Locations.Select(l => scale.FromHfaEquivalent(normative.ExpectedHfa(l.Index, settings.AgeYears))).ToList();
            var factory = new ProcedureFactory(settings.Strategy, cfg.Strategies, scale);
            _strategy = new ThresholdStrategy(_grid, factory, expected, cfg.Strategies.Growth.UseNeighbourSeeding, _rng.Fork());

            if (settings.FixationMode == FixationMode.CentralHeijlKrakau)
                _bsLocator = new BlindSpotLocator(cfg.Reliability.BlindSpot);

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

        private int Draw(CatchTrialConfig c) => Math.Max(1, c.EveryN + _rng.NextInt(-c.Jitter, c.Jitter + 1));

        public bool IsFinished => _pending == null && (_bsLocator == null || _bsLocator.IsFinished) && _strategy.IsFinished;

        public PresentationRequest Next()
        {
            if (_pending != null) throw new InvalidOperationException("Submit the pending presentation first");
            if (IsFinished) return null;

            if (_bsLocator != null && !_bsLocator.IsFinished)
                return Issue(PresentationKind.BlindSpotMapping, -1, _bsLocator.Current, _bsLocator.StimulusDb);

            var rel = _cfg.Reliability;
            if (_s.FalsePositiveCatchTrials && _toFp <= 0)
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
                DurationMs = _cfg.Stimulus.DurationMs,
                ResponseWindowMs = t.ResponseWindowMs,
                PreStimulusDelayMs = _rng.NextInt(t.IsiMinMs, t.IsiMaxMs + 1),
            };
            return _pending;
        }

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
            bool anticipated = result.Responded && result.LatencyMs.HasValue && result.LatencyMs.Value < t.MinValidLatencyMs;
            bool inWindow = result.Responded && (!result.LatencyMs.HasValue || result.LatencyMs.Value <= t.ResponseWindowMs);
            bool seen = inWindow && !anticipated;
            var rel = _record.Reliability;
            string disposition;

            switch (req.Kind)
            {
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
                    if (result.Responded && !anticipated) rel.FalsePositives.Errors++;
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
                    if (anticipated) { rel.AnticipatedResponses++; disposition = "anticipated"; }
                    else if (!result.FixationOk) { rel.GazeRejectedPresentations++; disposition = "fixationLost"; }
                    else
                    {
                        _strategy.Update(req.LocationIndex, req.Db, seen);
                        disposition = "used";
                    }
                    _toFp--; _toFn--; _toFl--;
                    break;
            }

            rel.TotalPresentations++;
            double end = result.OnsetMs + (seen && result.LatencyMs.HasValue ? result.LatencyMs.Value : t.ResponseWindowMs);
            rel.DurationMs = Math.Max(rel.DurationMs, end);
            _record.Presentations.Add(new PresentationLogEntry { Request = req, Result = result, Disposition = disposition });
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
            return _record;
        }
    }
}
