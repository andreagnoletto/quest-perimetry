using System;
using System.Collections.Generic;
using System.Linq;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;

namespace Perimetry.Headset.Logic
{
    public enum FlowState
    {
        Welcome, SetupPreset, SetupAge, Instructions,
        EyeIntro, SharpnessCheck, OcclusionIntro, OcclusionWindow, OcclusionFailed,
        TrainingIntro, TrainingTrial, TrainingFeedback, TrainingRepeat,
        ExamReady, Exam, Paused, Resuming, BetweenEyes,
        Done, Aborted, SaveError,
    }

    /// <summary>A stimulus to draw this frame, in the tested eye's field coordinates (patient's view, degrees).</summary>
    public sealed class StimulusDraw
    {
        public Eye Eye;
        public double FieldX, FieldY, DiameterDeg, LuminanceCdm2;
    }

    /// <summary>Everything the renderer needs for one frame. The renderer draws exactly this and nothing else.</summary>
    public sealed class FlowView
    {
        public FlowState State;
        /// <summary>When set, background, fixation, text and stimulus are drawn to this eye only; the other eye gets <see cref="UntestedEyeCdm2"/>.</summary>
        public Eye? MonocularEye;
        public double BackgroundCdm2;
        public double UntestedEyeCdm2;
        public bool ShowFixation;
        public double FixationLuminanceCdm2;
        public double FixationSizeDeg;
        public string MessageKey;
        public string Message;
        public StimulusDraw Stimulus;
        /// <summary>Set on the frame the message changes: the key of the voice clip to play (optional asset).</summary>
        public string AudioCue;
    }

    /// <summary>Device facts and clocks the flow cannot know by itself.</summary>
    public sealed class FlowEnvironment
    {
        public string DeviceModel = "unknown";
        public string DeviceSerial = "unknown";
        public Func<DateTime> UtcNow = () => DateTime.UtcNow;
        /// <summary>Normative database per grid; defaults to the provisional synthetic model.</summary>
        public Func<ClinicalConfig, TestGrid, INormativeDatabase> Normative = (c, g) => ProvisionalNormative.For(c, g);
    }

    /// <summary>
    /// The complete patient flow on the headset (SPEC §8), as a pure state machine. The Unity layer feeds it
    /// button edges, presence changes and one <see cref="Tick"/> per rendered frame (with the frame's
    /// predicted display time) and renders <see cref="View"/>.
    /// Welcome (button check) -> operator setup (preset, age) unless an exam order supplies them ->
    /// instructions -> per eye: intro, sharpness check, occlusion check, training (first eye only, 3-5 trials
    /// with feedback), ready, exam (blind-spot mapping + 24-2 etc. inside <see cref="ExamSession"/>) ->
    /// rest between eyes -> "Exame concluído. Aguarde o médico." No result is ever shown to the patient.
    /// Holding the button during the exam pauses it (the presentation in progress is discarded); taking the
    /// headset off pauses it too.
    /// </summary>
    public sealed class PatientFlow
    {
        public const string Version = "0.2.0-phase2";

        private readonly ClinicalConfig _cfg;
        private readonly HeadsetConfig _h;
        private readonly LuminanceMapper _lum;
        private readonly IExamSink _sink;
        private readonly FlowEnvironment _env;
        private readonly ExamOrder _order;
        private readonly Rng _trainingRng;
        private readonly string _culture;
        private readonly List<ExamRecord> _exams = new List<ExamRecord>();

        private double? _t0;
        private double _now;
        private bool _down, _holdFired, _present = true;
        private double _downAt;
        private double _stateUntil;

        private int _presetIndex;
        private double _age;
        private List<Eye> _eyes;
        private int _eyeIndex;
        private bool _trained;

        private int _trainingRound, _trainingIndex, _trainingSeq;
        private PresentationTimeline _timeline;
        private string _feedbackKey;

        private ExamSession _session;
        private double _examStart;
        private double _pauseStart;
        private string _pauseReason;
        private string _lastMessageKey, _pendingCue;

        public PatientFlow(ClinicalConfig cfg, HeadsetConfig headset, LuminanceMapper luminance, IExamSink sink,
            ExamOrder order = null, FlowEnvironment env = null)
        {
            _cfg = cfg; _h = headset; _lum = luminance; _sink = sink;
            _order = order ?? new ExamOrder();
            _env = env ?? new FlowEnvironment();
            _culture = headset.Language;
            _age = _order.AgeYears ?? headset.DefaultAgeYears;
            if (_order.PresetId != null) _presetIndex = headset.Presets.FindIndex(p => p.Id == _order.PresetId);
            if (_presetIndex < 0) throw new ArgumentException("exam order names an unknown preset: " + _order.PresetId);
            Seed = _order.Seed ?? (ulong)_env.UtcNow().Ticks;
            _trainingRng = new Rng(Seed ^ 0x5EED_7EA1UL);
            Log = new SessionLog
            {
                SoftwareVersion = ExamSession.Version + "/" + Version,
                SessionId = Guid.NewGuid().ToString("N").Substring(0, 12),
                StartedUtc = _env.UtcNow(),
                ConfigHash = cfg.Hash,
                DeviceModel = _env.DeviceModel,
                DeviceSerial = _env.DeviceSerial,
                RefreshRateHz = cfg.Display.RefreshRateHz,
                Calibrated = luminance.IsCalibrated,
                CalibrationRef = luminance.CalibrationRef,
                Order = _order,
            };
            RefreshRateHz = cfg.Display.RefreshRateHz;
            Event("state", State.ToString());
            View = BuildView();
        }

        public FlowState State { get; private set; } = FlowState.Welcome;
        public FlowView View { get; private set; }
        public SessionLog Log { get; }
        public ulong Seed { get; }
        public ExamPreset Preset => _h.Presets[_presetIndex];
        public double AgeYears => _age;
        public Eye? CurrentEye => _eyes != null && _eyeIndex < _eyes.Count ? _eyes[_eyeIndex] : (Eye?)null;
        public ExamSession CurrentSession => _session;
        /// <summary>Presentation in progress (training or exam), for the operator display and simulators.</summary>
        public PresentationTimeline CurrentPresentation => _timeline;
        public IReadOnlyList<ExamRecord> CompletedExams => _exams;
        /// <summary>
        /// Refresh rate used to quantise stimulus duration to frames. Starts at config display.refreshRateHz;
        /// the Unity layer replaces it with the measured rate (the Quest runs at 72 Hz unless 90/120 is granted).
        /// </summary>
        public double RefreshRateHz { get; private set; }

        public void SetMeasuredRefreshRate(double hz)
        {
            if (hz <= 0 || double.IsNaN(hz)) throw new ArgumentOutOfRangeException(nameof(hz));
            if (_session != null || _timeline != null) return; // never change mid-presentation or mid-exam
            RefreshRateHz = hz;
            Log.RefreshRateHz = hz;
            Event("refreshRate", hz.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)
                + (Math.Abs(hz - _cfg.Display.RefreshRateHz) / _cfg.Display.RefreshRateHz > 0.05 ? " (differs from config " + _cfg.Display.RefreshRateHz + ")" : ""));
        }

        /// <summary>Records a fact observed by the presentation layer (eye-position source, IPD, device state).</summary>
        public void Note(string type, string detail) => Event(type, detail);

        public bool IsFinished => State == FlowState.Done || State == FlowState.Aborted || State == FlowState.SaveError;

        // ---------------------------------------------------------------- inputs

        public void ButtonDown(double tMs)
        {
            Clock(tMs);
            if (_down) return;
            _down = true; _downAt = tMs; _holdFired = false;
            switch (State)
            {
                case FlowState.TrainingTrial:
                case FlowState.Exam:
                    _timeline?.Press(tMs);
                    break;
                case FlowState.OcclusionWindow:
                    Event("occlusionCheck", "failed: light seen with tested eye closed");
                    Go(FlowState.OcclusionFailed);
                    _holdFired = true; // this press must not also count as a tap
                    break;
            }
        }

        public void ButtonUp(double tMs)
        {
            Clock(tMs);
            if (!_down) return;
            _down = false;
            if (!_holdFired) Tap(tMs);
        }

        /// <summary>Operator thumbstick / arrow keys on the setup screens.</summary>
        public void OperatorAdjust(int delta)
        {
            if (State == FlowState.SetupAge) { _age = Math.Max(1, Math.Min(110, _age + delta)); View = BuildView(); }
            else if (State == FlowState.SetupPreset && delta != 0)
            {
                _presetIndex = ((_presetIndex + Math.Sign(delta)) % _h.Presets.Count + _h.Presets.Count) % _h.Presets.Count;
                View = BuildView();
            }
        }

        /// <summary>Headset worn / removed (proximity sensor). Removing it during the exam pauses it.</summary>
        public void SetUserPresent(bool present, double tMs)
        {
            Clock(tMs);
            if (present == _present) return;
            _present = present;
            Event("userPresence", present ? "on" : "off");
            if (!present)
            {
                if (State == FlowState.Exam) Pause("headsetRemoved");
                else if (State == FlowState.Resuming) { State = FlowState.Paused; Event("state", State.ToString()); }
            }
            View = BuildView();
        }

        /// <summary>Operator stops the session. A running eye exam is saved with Aborted = true.</summary>
        public void OperatorAbort(double tMs)
        {
            Clock(tMs);
            if (IsFinished) return;
            Event("operatorAbort", State.ToString());
            if (_session != null)
            {
                if (_timeline != null) { _session.Submit(_timeline.Aborted(_examStart, _now)); _timeline = null; }
                if (State == FlowState.Paused || State == FlowState.Resuming)
                    _session.RecordPause(_pauseStart - _examStart, _now - _examStart, _pauseReason);
                var rec = _session.BuildRecord();
                rec.Aborted = true;
                _session = null;
                if (!Save(rec)) return;
            }
            Finish("aborted", FlowState.Aborted);
        }

        public void Tick(double nowMs)
        {
            Clock(nowMs);
            if (_down && !_holdFired && nowMs - _downAt >= HoldMs())
            {
                _holdFired = true;
                Hold();
            }

            switch (State)
            {
                case FlowState.OcclusionWindow:
                    if (nowMs >= _stateUntil)
                    {
                        Event("occlusionCheck", "passed");
                        Go(!_trained ? FlowState.TrainingIntro : FlowState.ExamReady);
                    }
                    break;
                case FlowState.TrainingTrial:
                    TickTraining();
                    break;
                case FlowState.TrainingFeedback:
                    if (nowMs >= _stateUntil) NextTrainingTrial();
                    break;
                case FlowState.Exam:
                    TickExam();
                    break;
                case FlowState.Resuming:
                    if (nowMs >= _stateUntil)
                    {
                        _session.RecordPause(_pauseStart - _examStart, nowMs - _examStart, _pauseReason);
                        Event("resume", _pauseReason);
                        Go(FlowState.Exam);
                    }
                    break;
            }
            View = BuildView(consumeCue: true);
        }

        // ---------------------------------------------------------------- transitions

        private int HoldMs() => State == FlowState.Exam ? _cfg.Timing.PauseHoldMs : _h.MenuHoldMs;

        private void Tap(double t)
        {
            switch (State)
            {
                case FlowState.Welcome:
                    Event("buttonCheck", "ok");
                    if (_order.PresetId == null) Go(FlowState.SetupPreset);
                    else if (_order.AgeYears == null) Go(FlowState.SetupAge);
                    else StartInstructions();
                    break;
                case FlowState.SetupPreset:
                    OperatorAdjust(+1);
                    break;
                case FlowState.Instructions:
                    Go(FlowState.EyeIntro);
                    break;
                case FlowState.EyeIntro:
                    Go(FlowState.SharpnessCheck);
                    break;
                case FlowState.SharpnessCheck:
                    Event("sharpnessCheck", "confirmed by patient");
                    Go(FlowState.OcclusionIntro);
                    break;
                case FlowState.OcclusionIntro:
                    _stateUntil = t + _h.Checks.OcclusionWindowMs;
                    Go(FlowState.OcclusionWindow);
                    break;
                case FlowState.TrainingIntro:
                    _trainingRound = 0;
                    StartTrainingRound();
                    break;
                case FlowState.TrainingRepeat:
                    _trainingRound++;
                    StartTrainingRound();
                    break;
                case FlowState.ExamReady:
                    StartExam(t);
                    break;
                case FlowState.Paused:
                    if (!_present) break;
                    _stateUntil = t + _cfg.Timing.ResumeAfterPauseDelayMs;
                    Go(FlowState.Resuming);
                    break;
                case FlowState.BetweenEyes:
                    Go(FlowState.EyeIntro);
                    break;
            }
        }

        private void Hold()
        {
            switch (State)
            {
                case FlowState.SetupPreset:
                    Event("preset", Preset.Id);
                    if (_order.AgeYears == null) Go(FlowState.SetupAge); else StartInstructions();
                    break;
                case FlowState.SetupAge:
                    Event("age", _age.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    StartInstructions();
                    break;
                case FlowState.OcclusionFailed:
                    Event("occlusionCheck", "retry by operator");
                    Go(FlowState.OcclusionIntro);
                    break;
                case FlowState.Exam:
                    Pause("patient");
                    break;
            }
        }

        private void StartInstructions()
        {
            Log.PresetId = Preset.Id;
            _eyes = _h.EyeOrder.Where(e => Preset.Eyes.Contains(e)).ToList();
            if (_eyes.Count == 0) throw new InvalidOperationException("preset " + Preset.Id + " tests no eye in eyeOrder");
            _eyeIndex = 0;
            Go(FlowState.Instructions);
        }

        private void Pause(string reason)
        {
            if (_timeline != null)
            {
                _session.Submit(_timeline.Aborted(_examStart, _now));
                Event("presentationAborted", reason);
                _timeline = null;
            }
            _pauseStart = _now;
            _pauseReason = reason;
            Event("pause", reason);
            Go(FlowState.Paused);
        }

        // ---------------------------------------------------------------- training

        private void StartTrainingRound()
        {
            _trainingIndex = 0;
            StartTrainingTrial();
        }

        private void StartTrainingTrial()
        {
            var tr = _h.Training.Trials[_trainingIndex];
            var eye = CurrentEye.Value;
            var p = new FieldPoint(tr.X, tr.Y).ForEye(eye);
            var scale = _lum.ScaleFor(eye);
            var t = _cfg.Timing;
            var req = new PresentationRequest
            {
                Sequence = ++_trainingSeq,
                Kind = PresentationKind.Threshold,
                LocationIndex = -1,
                FieldX = p.X,
                FieldY = p.Y,
                StimulusShown = tr.StimulusShown,
                Db = tr.StimulusShown ? tr.Db : double.NaN,
                LuminanceCdm2 = tr.StimulusShown ? scale.DbToStimulusLuminance(tr.Db) : scale.BackgroundCdm2,
                IncrementCdm2 = tr.StimulusShown ? scale.DbToIncrement(tr.Db) : 0,
                Size = Preset.StimulusSize,
                SizeDeg = Core.Geometry.Stimulus.DiameterDeg(_cfg.Stimulus, Preset.StimulusSize),
                DurationMs = _cfg.Stimulus.DurationMs,
                ResponseWindowMs = t.ResponseWindowMs,
                PreStimulusDelayMs = _trainingRng.NextInt(t.IsiMinMs, t.IsiMaxMs + 1),
            };
            _timeline = new PresentationTimeline(req, _now, RefreshRateHz);
            Go(FlowState.TrainingTrial);
        }

        private void TickTraining()
        {
            _timeline.Tick(_now);
            if (!_timeline.IsDone || _down) return;
            var tr = _h.Training.Trials[_trainingIndex];
            bool responded = _timeline.ResponseMs.HasValue;
            bool correct = responded == tr.StimulusShown;
            Log.Training.Add(new TrainingTrialLog
            {
                Eye = CurrentEye.Value,
                Round = _trainingRound,
                Trial = tr,
                Responded = responded,
                LatencyMs = responded ? _timeline.ResponseMs - _timeline.OnsetMs : null,
                EarlyPresses = _timeline.EarlyPresses,
                Correct = correct,
            });
            _feedbackKey = tr.StimulusShown
                ? (responded ? "training.hit" : "training.miss")
                : (responded ? "training.falseAlarm" : "training.correctRejection");
            _timeline = null;
            _stateUntil = _now + _h.Training.FeedbackMs;
            Go(FlowState.TrainingFeedback);
        }

        private void NextTrainingTrial()
        {
            _trainingIndex++;
            if (_trainingIndex < _h.Training.Trials.Count) { StartTrainingTrial(); return; }
            int wrong = Log.Training.Count(l => l.Round == _trainingRound && !l.Correct);
            Event("training", $"round {_trainingRound}: {wrong} incorrect of {_h.Training.Trials.Count}");
            if (wrong > _h.Training.RepeatIfMissedMoreThan && _trainingRound < _h.Training.MaxRepeats)
            {
                Go(FlowState.TrainingRepeat);
                return;
            }
            _trained = true;
            Go(FlowState.ExamReady);
        }

        // ---------------------------------------------------------------- exam

        private void StartExam(double t)
        {
            var eye = CurrentEye.Value;
            var grid = GridLibrary.Get(_cfg, Preset.GridId);
            var settings = new ExamSettings
            {
                GridId = Preset.GridId,
                Eye = eye,
                Strategy = Preset.Strategy,
                FixationMode = Preset.FixationMode,
                StimulusSize = Preset.StimulusSize,
                AgeYears = _age,
                Seed = Seed + (ulong)_eyeIndex,
                PatientRef = _order.PatientRef,
                RefractiveCorrection = _order.RefractiveCorrection,
            };
            _session = new ExamSession(settings, _cfg, _env.Normative(_cfg, grid), _lum.ScaleFor(eye), _lum.CalibrationRef);
            _examStart = t;
            Event("examStart", $"{eye} {Preset.GridId} {Preset.Strategy} seed {settings.Seed}");
            Go(FlowState.Exam);
            _timeline = null;
        }

        private void TickExam()
        {
            if (_timeline == null && !NextPresentation()) return;
            _timeline.Tick(_now);
            if (!_timeline.IsDone || _down) return;
            if (_timeline.EarlyPresses > 0) Event("earlyPress", $"seq {_timeline.Request.Sequence}: {_timeline.EarlyPresses}");
            _session.Submit(_timeline.Result(_examStart));
            _timeline = null;
            NextPresentation();
        }

        /// <summary>Requests the next presentation (its ISI starts now); finishes the eye when there is none.</summary>
        private bool NextPresentation()
        {
            var req = _session.Next();
            if (req == null) { FinishEye(); return false; }
            _timeline = new PresentationTimeline(req, _now, RefreshRateHz);
            return true;
        }

        private void FinishEye()
        {
            var rec = _session.BuildRecord();
            _session = null;
            Event("examEnd", rec.ExamId);
            if (!Save(rec)) return;
            _eyeIndex++;
            if (_eyeIndex < _eyes.Count) Go(FlowState.BetweenEyes);
            else Finish("completed", FlowState.Done);
        }

        private bool Save(ExamRecord rec)
        {
            _exams.Add(rec);
            Log.ExamIds.Add(rec.ExamId);
            try { _sink.SaveExam(rec); return true; }
            catch (Exception e)
            {
                Event("saveError", e.GetType().Name + ": " + e.Message);
                Go(FlowState.SaveError);
                return false;
            }
        }

        private void Finish(string outcome, FlowState final)
        {
            Log.Outcome = outcome;
            Event("finish", outcome);
            try { _sink.SaveSessionLog(Log); Go(final); }
            catch (Exception e) { Event("saveError", e.GetType().Name + ": " + e.Message); Go(FlowState.SaveError); }
        }

        // ---------------------------------------------------------------- helpers

        private void Clock(double tMs)
        {
            if (_t0 == null) _t0 = tMs;
            _now = Math.Max(_now, tMs);
        }

        private void Go(FlowState s)
        {
            State = s;
            Event("state", s.ToString());
            View = BuildView();
        }

        private void Event(string type, string detail) =>
            Log.Events.Add(new SessionEvent { TMs = _t0.HasValue ? _now - _t0.Value : 0, Type = type, Detail = detail });

        private static bool IsMonocular(FlowState s) =>
            s == FlowState.EyeIntro || s == FlowState.SharpnessCheck || s == FlowState.OcclusionIntro || s == FlowState.OcclusionWindow
            || s == FlowState.TrainingIntro || s == FlowState.TrainingTrial || s == FlowState.TrainingFeedback || s == FlowState.TrainingRepeat
            || s == FlowState.ExamReady || s == FlowState.Exam || s == FlowState.Paused || s == FlowState.Resuming;

        private static bool ShowsFixation(FlowState s) =>
            s == FlowState.SharpnessCheck || s == FlowState.TrainingTrial || s == FlowState.TrainingFeedback
            || s == FlowState.ExamReady || s == FlowState.Exam || s == FlowState.Paused || s == FlowState.Resuming;

        private string MessageKeyFor(FlowState s)
        {
            switch (s)
            {
                case FlowState.Welcome: return "welcome";
                case FlowState.SetupPreset: return "setup.preset";
                case FlowState.SetupAge: return "setup.age";
                case FlowState.Instructions: return "instructions";
                case FlowState.EyeIntro: return "eye.intro";
                case FlowState.SharpnessCheck: return "check.sharpness";
                case FlowState.OcclusionIntro: return "check.occlusion.intro";
                case FlowState.OcclusionFailed: return "check.occlusion.failed";
                case FlowState.TrainingIntro: return "training.intro";
                case FlowState.TrainingFeedback: return _feedbackKey;
                case FlowState.TrainingRepeat: return "training.repeat";
                case FlowState.ExamReady: return "exam.ready";
                case FlowState.Paused: return _pauseReason == "headsetRemoved" ? "pause.headsetRemoved" : "pause";
                case FlowState.Resuming: return null;
                case FlowState.BetweenEyes: return "betweenEyes";
                case FlowState.Done: return "done";
                case FlowState.Aborted: return "aborted";
                case FlowState.SaveError: return "error.save";
                default: return null;
            }
        }

        /// <param name="consumeCue">True only for the per-frame view built by <see cref="Tick"/>, so a cue raised by an input edge is still delivered once.</param>
        private FlowView BuildView(bool consumeCue = false)
        {
            var s = State;
            var key = MessageKeyFor(s);
            string text = null;
            if (key != null)
            {
                switch (key)
                {
                    case "setup.preset": text = PatientMessages.Get(key, _culture, Preset.Label); break;
                    case "setup.age": text = PatientMessages.Get(key, _culture, _age); break;
                    case "eye.intro": text = PatientMessages.Get(key, _culture, PatientMessages.EyeName(CurrentEye.Value, _culture)); break;
                    case "check.occlusion.intro": text = PatientMessages.Get(key, _culture, PatientMessages.EyeName(CurrentEye.Value, _culture)); break;
                    default: text = PatientMessages.Get(key, _culture); break;
                }
            }

            var v = new FlowView
            {
                State = s,
                MonocularEye = IsMonocular(s) ? CurrentEye : null,
                BackgroundCdm2 = _cfg.Display.BackgroundCdm2,
                UntestedEyeCdm2 = _cfg.Display.UntestedEyeLuminanceCdm2,
                ShowFixation = ShowsFixation(s),
                FixationLuminanceCdm2 = _h.FixationTarget.LuminanceCdm2,
                FixationSizeDeg = _h.FixationTarget.SizeDeg,
                MessageKey = key,
                Message = text,
            };
            if (key != _lastMessageKey) { _pendingCue = key; _lastMessageKey = key; }
            v.AudioCue = _pendingCue;
            if (consumeCue) _pendingCue = null;

            if (s == FlowState.OcclusionWindow)
            {
                var eye = CurrentEye.Value;
                v.Stimulus = new StimulusDraw
                {
                    Eye = eye, FieldX = 0, FieldY = 0, DiameterDeg = _h.Checks.OcclusionDiscDeg,
                    LuminanceCdm2 = _lum.ScaleFor(eye).DbToStimulusLuminance(0),
                };
            }
            else if (_timeline != null && _timeline.StimulusVisible && (s == FlowState.Exam || s == FlowState.TrainingTrial))
            {
                var r = _timeline.Request;
                v.Stimulus = new StimulusDraw
                {
                    Eye = CurrentEye.Value, FieldX = r.FieldX, FieldY = r.FieldY, DiameterDeg = r.SizeDeg, LuminanceCdm2 = r.LuminanceCdm2,
                };
            }
            return v;
        }
    }
}
