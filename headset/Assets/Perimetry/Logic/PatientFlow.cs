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
        Welcome, SetupOrder, SetupPreset, SetupAge, Instructions,
        EyeIntro, SharpnessCheck, OcclusionIntro, OcclusionWindow, OcclusionFailed,
        TrainingIntro, TrainingTrial, TrainingFeedback, TrainingRepeat,
        ExamReady, Exam, Paused, Resuming, BetweenEyes,
        Done, Aborted, SaveError,
    }

    /// <summary>
    /// A stimulus to draw this frame, in the tested eye's field coordinates (patient's view, degrees, from straight
    /// ahead). In oculokinetic mode this is already the composed direction (target ⊕ retinal offset).
    /// </summary>
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
        /// <summary>Central point or the four-dot diamond of the foveal test (Phase 5).</summary>
        public FixationTargetKind FixationTarget = FixationTargetKind.Central;
        /// <summary>Target direction from straight ahead (degrees, tested eye's view); (0, 0) except in oculokinetic mode.</summary>
        public double FixationX, FixationY;
        /// <summary>Scale and luminance factor of the target during the oculokinetic lock animation (1 otherwise).</summary>
        public double FixationScale = 1, FixationLuminanceFactor = 1;
        /// <summary>Diamond4: distance from the centre to each dot, and the dot diameter.</summary>
        public double DiamondHalfSizeDeg, DiamondDotSizeDeg;
        /// <summary>Forced choice: the target is blinked off on this frame to mark an interval.</summary>
        public bool FixationMarker;
        /// <summary>Eye tracking data is arriving (Quest Pro); false on Quest 3.</summary>
        public bool GazeAvailable;
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
    /// headset off pauses it too. The portal (Phase 4) can pause, resume and abort through
    /// <see cref="OperatorPause"/>, <see cref="OperatorResume"/> and <see cref="OperatorAbort"/>; a remote pause
    /// is exactly the patient's hold with another reason, so the psychophysics are unchanged.
    /// </summary>
    public sealed class PatientFlow
    {
        public const string Version = "0.3.0-phase5";

        private readonly ClinicalConfig _cfg;
        private readonly HeadsetConfig _h;
        private readonly LuminanceMapper _lum;
        private readonly IExamSink _sink;
        private readonly FlowEnvironment _env;
        private ExamOrder _order;
        private readonly bool _orderFromFile;
        private readonly List<ExamOrder> _pendingOrders = new List<ExamOrder>();
        private int _orderIndex;
        private ulong _examSeed;
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
        private ExamSettings _examSettings;
        private double _examStart;
        private double _pauseStart;
        private string _pauseReason;
        private string _lastMessageKey, _pendingCue;
        private readonly GazeMonitor _gaze;
        private bool _gazeWasAvailable;

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
            _orderFromFile = _order.PresetId != null || _order.PatientRef != null || _order.AgeYears != null;
            Seed = _order.Seed ?? (ulong)_env.UtcNow().Ticks;
            _examSeed = Seed;
            _trainingRng = new Rng(Seed ^ 0x5EED_7EA1UL);
            _gaze = new GazeMonitor(headset.Oculokinetic.GazeStaleMs);
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
        /// <summary>Settings of the exam in progress (null outside the exam).</summary>
        public ExamSettings CurrentSettings => _session != null ? _examSettings : null;
        /// <summary>The order in effect: from exam-order.json, chosen on the <see cref="FlowState.SetupOrder"/> screen, or empty (manual setup).</summary>
        public ExamOrder ActiveOrder => _order;
        /// <summary>Orders offered on the SetupOrder screen (from the portal); the extra last option is "manual".</summary>
        public IReadOnlyList<ExamOrder> PendingOrders => _pendingOrders;
        /// <summary>Incremented on every state change; lets a poller notice transitions without diffing.</summary>
        public int StateChanges { get; private set; }
        /// <summary>ms since the first input/tick, for the live panel.</summary>
        public double ElapsedMs => _t0.HasValue ? _now - _t0.Value : 0;
        public DateTime SessionStartedUtc => Log.StartedUtc;
        public bool IsPaused => State == FlowState.Paused || State == FlowState.Resuming;
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

        /// <summary>
        /// Eye tracker sample (Phase 5): gaze direction of the tested eye as absolute field coordinates (degrees from
        /// straight ahead, patient's view). Call once per frame before <see cref="Tick"/>; on a headset without an eye
        /// tracker never call it and nothing changes.
        /// </summary>
        public void GazeSample(double tMs, double fieldX, double fieldY)
        {
            Clock(tMs);
            _gaze.Sample(tMs, new FieldPoint(fieldX, fieldY));
        }

        /// <summary>True while gaze samples keep arriving (headset.json oculokinetic.gazeStaleMs).</summary>
        public bool GazeAvailable => _gaze.Available(_now);

        public bool IsFinished => State == FlowState.Done || State == FlowState.Aborted || State == FlowState.SaveError;

        /// <summary>
        /// Orders fetched from the portal, offered to the operator after the welcome tap. Ignored once the flow
        /// has left the welcome screen or when an exam-order file already fixed the order. Orders naming a
        /// preset this headset does not have are dropped (logged).
        /// </summary>
        public void SetPendingOrders(IReadOnlyList<ExamOrder> orders)
        {
            if (State != FlowState.Welcome || _orderFromFile) return;
            _pendingOrders.Clear();
            foreach (var o in orders ?? new List<ExamOrder>())
            {
                if (o == null) continue;
                if (o.PresetId != null && _h.Presets.FindIndex(p => p.Id == o.PresetId) < 0) { Event("order", "skipped " + o.Id + ": unknown preset " + o.PresetId); continue; }
                _pendingOrders.Add(o);
            }
            _orderIndex = 0;
            Event("orders", _pendingOrders.Count + " pending from portal");
            View = BuildView();
        }

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
            else if (State == FlowState.SetupOrder && delta != 0)
            {
                int n = _pendingOrders.Count + 1; // + "manual"
                _orderIndex = ((_orderIndex + Math.Sign(delta)) % n + n) % n;
                View = BuildView();
            }
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

        /// <summary>Doctor pauses from the portal: same as the patient holding the button, reason "operator".</summary>
        public void OperatorPause(double tMs)
        {
            Clock(tMs);
            if (State != FlowState.Exam) return;
            Pause("operator");
        }

        /// <summary>Doctor resumes from the portal: same as the patient's tap on the pause screen; ignored while the headset is off.</summary>
        public void OperatorResume(double tMs)
        {
            Clock(tMs);
            if (State != FlowState.Paused || !_present) return;
            Event("operatorResume", _pauseReason);
            _stateUntil = tMs + _cfg.Timing.ResumeAfterPauseDelayMs;
            Go(FlowState.Resuming);
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
            bool gazeNow = _gaze.Available(nowMs);
            if (gazeNow != _gazeWasAvailable) { _gazeWasAvailable = gazeNow; Event("gaze", gazeNow ? "available" : "unavailable"); }
            if (_timeline != null)
            {
                var r = _timeline.Request;
                _timeline.Gaze(nowMs, _gaze.ErrorDeg(nowMs, new FieldPoint(r.TargetX, r.TargetY)));
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
                    if (_pendingOrders.Count > 0 && !_orderFromFile) Go(FlowState.SetupOrder);
                    else AfterOrder();
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
                    if (_h.Checks.OcclusionCheckEnabled) Go(FlowState.OcclusionIntro);
                    else Go(!_trained ? FlowState.TrainingIntro : FlowState.ExamReady);
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
                case FlowState.SetupOrder:
                    if (_orderIndex < _pendingOrders.Count) ApplyOrder(_pendingOrders[_orderIndex]);
                    else Event("order", "manual");
                    AfterOrder();
                    break;
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

        /// <summary>Welcome / order screen done: ask the operator only for what the order does not provide.</summary>
        private void AfterOrder()
        {
            if (_order.PresetId == null) Go(FlowState.SetupPreset);
            else if (_order.AgeYears == null) Go(FlowState.SetupAge);
            else StartInstructions();
        }

        private void ApplyOrder(ExamOrder o)
        {
            _order = o;
            Log.Order = o;
            if (o.AgeYears != null) _age = o.AgeYears.Value;
            if (o.PresetId != null) _presetIndex = _h.Presets.FindIndex(p => p.Id == o.PresetId);
            // The session seed (training PRNG, session id) was fixed at start-up from the clock; an order seed
            // applies to the exams themselves, which is what replay needs (ExamSettings.Seed is in the record).
            _examSeed = o.Seed ?? Seed;
            Event("order", o.Id ?? "(no id)");
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
                Seed = _examSeed + (ulong)_eyeIndex,
                PatientRef = _order.PatientRef,
                RefractiveCorrection = _order.RefractiveCorrection,
                FovealThreshold = Preset.FovealThreshold,
                ResponseMode = Preset.ResponseMode,
            };
            _session = new ExamSession(settings, _cfg, _env.Normative(_cfg, grid), _lum.ScaleFor(eye), _lum.CalibrationRef);
            _examSettings = settings;
            _examStart = t;
            Event("examStart", $"{eye} {Preset.GridId} {Preset.Strategy} {settings.FixationMode} {settings.ResponseMode}" +
                $"{(settings.FovealThreshold ? " foveal" : "")} seed {settings.Seed} gaze {(_gaze.Available(t) ? "available" : "unavailable")}");
            if (settings.FixationMode == FixationMode.CentralEyeTracking && !_gaze.Available(t))
                Event("gaze", "CentralEyeTracking without eye tracking data: fixation is unmonitored for this eye");
            Go(FlowState.Exam);
            _timeline = null;
        }

        /// <summary>Headset-side timing options for the next presentation (mode B lock, gaze confirmation).</summary>
        private TimelineOptions OptionsFor(PresentationRequest req)
        {
            var o = _cfg.Reliability.Oculokinetic;
            bool oculokinetic = _examSettings.FixationMode == FixationMode.Oculokinetic && req.Kind != PresentationKind.Foveal;
            return new TimelineOptions
            {
                Oculokinetic = oculokinetic,
                LockDelayMs = o.LockDelayMs,
                GazeAvailable = _gaze.Available(_now),
                GazeConfirmMs = o.GazeConfirmMs,
                GazeConfirmDeg = o.GazeConfirmDeg,
                GazeConfirmTimeoutMs = o.LockDelayMs * _h.Oculokinetic.GazeConfirmTimeoutFactor,
            };
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
            _timeline = new PresentationTimeline(req, _now, RefreshRateHz, OptionsFor(req));
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
            StateChanges++;
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
                case FlowState.SetupOrder: return "setup.order";
                case FlowState.SetupPreset: return "setup.preset";
                case FlowState.SetupAge: return "setup.age";
                case FlowState.EyeIntro: return "eye.intro";
                case FlowState.SharpnessCheck: return "check.sharpness";
                case FlowState.OcclusionIntro: return "check.occlusion.intro";
                case FlowState.OcclusionFailed: return "check.occlusion.failed";
                case FlowState.TrainingIntro: return "training.intro";
                case FlowState.TrainingFeedback: return _feedbackKey;
                case FlowState.TrainingRepeat: return "training.repeat";
                case FlowState.ExamReady:
                    return Preset.ResponseMode == ResponseMode.ForcedChoice ? "exam.ready.forcedChoice"
                        : Preset.FixationMode == FixationMode.Oculokinetic ? "exam.ready.oculokinetic"
                        : Preset.FovealThreshold ? "exam.ready.foveal" : "exam.ready";
                case FlowState.Instructions:
                    return Preset.ResponseMode == ResponseMode.ForcedChoice ? "instructions.forcedChoice"
                        : Preset.FixationMode == FixationMode.Oculokinetic ? "instructions.oculokinetic" : "instructions";
                case FlowState.Paused: return _pauseReason == "headsetRemoved" ? "pause.headsetRemoved" : "pause";
                case FlowState.Resuming: return null;
                case FlowState.BetweenEyes: return "betweenEyes";
                case FlowState.Done: return "done";
                case FlowState.Aborted: return "aborted";
                case FlowState.SaveError: return "error.save";
                default: return null;
            }
        }

        private string OrderLabel()
        {
            if (_orderIndex >= _pendingOrders.Count) return PatientMessages.Get("setup.order.none", _culture);
            var o = _pendingOrders[_orderIndex];
            var preset = _h.Presets.Find(p => p.Id == o.PresetId);
            return PatientMessages.Get("setup.order.item", _culture, o.PatientRef ?? "—", preset?.Label ?? o.PresetId ?? "—",
                o.AgeYears.HasValue ? o.AgeYears.Value.ToString("0", System.Globalization.CultureInfo.InvariantCulture) : "—");
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
                    case "setup.order": text = PatientMessages.Get(key, _culture, OrderLabel()); break;
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
                DiamondHalfSizeDeg = _cfg.Strategies.Foveal.DiamondHalfSizeDeg,
                DiamondDotSizeDeg = _h.FixationTarget.DiamondDotSizeDeg,
                GazeAvailable = _gaze.Available(_now),
                MessageKey = key,
                Message = text,
            };
            if (key != _lastMessageKey) { _pendingCue = key; _lastMessageKey = key; }
            v.AudioCue = _pendingCue;
            if (consumeCue) _pendingCue = null;

            // Fixation target of the presentation in progress (Phase 5): kind, position (mode B), lock animation, marker.
            var examTimeline = s == FlowState.Exam ? _timeline : null;
            if (examTimeline != null)
            {
                var r = examTimeline.Request;
                v.FixationTarget = r.FixationTarget;
                v.FixationX = r.TargetX;
                v.FixationY = r.TargetY;
                v.FixationScale = FixationLayout.LockScale(examTimeline.LockProgress, _h.FixationTarget.LockAnimation);
                v.FixationLuminanceFactor = FixationLayout.LockLuminanceFactor(examTimeline.LockProgress, _h.FixationTarget.LockAnimation);
                v.FixationMarker = examTimeline.FixationMarker;
                if (v.FixationMarker) v.ShowFixation = false;
            }
            else if (s == FlowState.ExamReady && Preset.FovealThreshold)
            {
                // The foveal test comes first: show the diamond the patient is being told about.
                v.FixationTarget = FixationTargetKind.Diamond4;
            }

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
                // Mode B: FieldX/FieldY are retinal offsets from the target; the drawn direction is their composition.
                var abs = AngularGeometry.Compose(new FieldPoint(r.TargetX, r.TargetY), new FieldPoint(r.FieldX, r.FieldY));
                v.Stimulus = new StimulusDraw
                {
                    Eye = CurrentEye.Value, FieldX = abs.X, FieldY = abs.Y, DiameterDeg = r.SizeDeg, LuminanceCdm2 = r.LuminanceCdm2,
                };
            }
            return v;
        }
    }
}
