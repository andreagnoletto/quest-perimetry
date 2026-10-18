using System;
using Perimetry.Core.Exam;

namespace Perimetry.Headset.Logic
{
    /// <summary>Headset-side presentation options that do not come with the request (Phase 5).</summary>
    public sealed class TimelineOptions
    {
        /// <summary>Mode B: the target jumps at the start of the presentation and a lock phase precedes the ISI.</summary>
        public bool Oculokinetic;
        /// <summary>Lock phase without eye tracking (reliability.json oculokinetic.lockDelayMs).</summary>
        public int LockDelayMs = 600;
        /// <summary>Eye tracking data is arriving: the lock ends when gaze is confirmed on the target.</summary>
        public bool GazeAvailable;
        public int GazeConfirmMs = 300;
        public double GazeConfirmDeg = 3;
        /// <summary>With eye tracking, give up waiting for the confirmation after this long and go on.</summary>
        public double GazeConfirmTimeoutMs = 3000;

        public static readonly TimelineOptions Default = new TimelineOptions();
    }

    /// <summary>
    /// Timing of one presentation, driven once per rendered frame. Times are the predicted display time
    /// of each frame (ms, monotonic). Sequence (mode A, yes/no): pre-stimulus interval (ISI) -> stimulus for a whole
    /// number of frames (duration quantised to the refresh rate) -> response window counted from onset.
    /// A press counts only if it comes at or after onset and within the window; earlier presses are counted
    /// in <see cref="EarlyPresses"/> and logged. The presentation ends at the first valid press (once the
    /// stimulus has finished) or when the window elapses.
    /// <para>
    /// Mode B (oculokinetic, Phase 5): the target is at its new position from the first frame; a <see cref="Phase.Lock"/>
    /// phase runs first (fixed <see cref="TimelineOptions.LockDelayMs"/>, or until gaze has stayed within
    /// GazeConfirmDeg of the target for GazeConfirmMs, with a timeout), then the random ISI, then the stimulus as in mode A.
    /// </para>
    /// <para>
    /// Forced choice (Phase 5): after the ISI, each of the N intervals starts with a <see cref="Phase.Marker"/> (target blink,
    /// MarkerMs) followed by the interval itself (IntervalMs, quantised to frames; the stimulus is drawn only in
    /// <c>StimulusInterval</c>), with IntervalGapMs between intervals; the response window follows the last interval.
    /// A press is attributed to the interval running when it happens (from its marker until the next marker; the window
    /// belongs to the last interval); the first press decides <see cref="RespondedInterval"/>. The anticipation rule does
    /// not apply (the core ignores latency in forced choice).
    /// </para>
    /// Gaze (Phase 5): the flow feeds one gaze error per frame through <see cref="Gaze"/>; the timeline keeps whether any
    /// sample arrived, the error on the onset frame and the largest error while the stimulus was on.
    /// </summary>
    public sealed class PresentationTimeline
    {
        public enum Phase { Lock, Isi, Marker, Stimulus, Gap, Window, Done }

        private readonly PresentationRequest _req;
        private readonly TimelineOptions _opt;
        private readonly double _startMs;
        private double _scheduledOnsetMs;
        private double _phaseUntil;
        private int _framesLeft;
        private double _windowStartMs;

        // gaze
        private double? _gazeErr;
        private double? _gazeWithinSince;

        public PresentationTimeline(PresentationRequest request, double startMs, double refreshRateHz, TimelineOptions options = null)
        {
            _req = request ?? throw new ArgumentNullException(nameof(request));
            if (refreshRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(refreshRateHz));
            _opt = options ?? TimelineOptions.Default;
            _startMs = startMs;
            FramesPlanned = Math.Max(1, (int)Math.Round(request.DurationMs * refreshRateHz / 1000.0));
            _framesLeft = FramesPlanned;
            if (_opt.Oculokinetic)
            {
                Current = Phase.Lock;
                _phaseUntil = startMs + (_opt.GazeAvailable ? _opt.GazeConfirmTimeoutMs : _opt.LockDelayMs);
            }
            else
            {
                Current = Phase.Isi;
                _scheduledOnsetMs = startMs + request.PreStimulusDelayMs;
            }
        }

        public PresentationRequest Request => _req;
        public Phase Current { get; private set; }
        public bool ForcedChoice => _req.Intervals > 1;
        /// <summary>Forced choice: 1-based interval in progress (0 before the first marker).</summary>
        public int CurrentInterval { get; private set; }
        /// <summary>True on frames where the stimulus must be drawn (never for a blank catch trial, only in the stimulus interval).</summary>
        public bool StimulusVisible => Current == Phase.Stimulus && _req.StimulusShown && (!ForcedChoice || CurrentInterval == _req.StimulusInterval);
        /// <summary>Forced choice: the fixation target blinks (is hidden) on these frames.</summary>
        public bool FixationMarker => Current == Phase.Marker;
        /// <summary>Frames the stimulus is on per interval (one interval in yes/no).</summary>
        public int FramesPlanned { get; }
        public int FramesShown { get; private set; }
        /// <summary>Onset of the stimulus (yes/no) or of the stimulus interval (forced choice).</summary>
        public double? OnsetMs { get; private set; }
        public double? ResponseMs { get; private set; }
        public int? RespondedInterval { get; private set; }
        public int EarlyPresses { get; private set; }
        public bool IsDone => Current == Phase.Done;
        /// <summary>When the lock phase ended (mode B): the ISI starts here.</summary>
        public double? LockEndMs { get; private set; }
        /// <summary>Mode B: 0..1 progress of the lock animation (1 once the lock has ended; 1 in mode A).</summary>
        public double LockProgress { get; private set; } = 1;

        // gaze statistics for the result
        public bool GazeTracked { get; private set; }
        public double? GazeErrorAtOnsetDeg { get; private set; }
        public double? GazeMaxErrorDeg { get; private set; }

        /// <summary>
        /// Gaze error (degrees from the presentation's target) for this frame, or null when no sample is available.
        /// Call before <see cref="Tick"/> each frame.
        /// </summary>
        public void Gaze(double tMs, double? errorDeg)
        {
            _gazeErr = errorDeg;
            if (!errorDeg.HasValue) { _gazeWithinSince = null; return; }
            GazeTracked = true;
            if (errorDeg.Value <= _opt.GazeConfirmDeg) { if (!_gazeWithinSince.HasValue) _gazeWithinSince = tMs; }
            else _gazeWithinSince = null;
        }

        public void Tick(double nowMs)
        {
            switch (Current)
            {
                case Phase.Lock:
                    TickLock(nowMs);
                    break;
                case Phase.Isi:
                    if (nowMs >= _scheduledOnsetMs)
                    {
                        if (ForcedChoice) StartMarker(nowMs, 1);
                        else StartStimulus(nowMs);
                    }
                    break;
                case Phase.Marker:
                    if (nowMs >= _phaseUntil) StartStimulus(nowMs);
                    break;
                case Phase.Stimulus:
                    if (_framesLeft > 0) CountStimulusFrame(nowMs);
                    else EndStimulus(nowMs);
                    break;
                case Phase.Gap:
                    if (nowMs >= _phaseUntil) StartMarker(nowMs, CurrentInterval + 1);
                    break;
                case Phase.Window:
                    CheckWindow(nowMs);
                    break;
            }
        }

        private void TickLock(double nowMs)
        {
            bool confirmed = _opt.GazeAvailable && _gazeWithinSince.HasValue && nowMs - _gazeWithinSince.Value >= _opt.GazeConfirmMs;
            if (_opt.GazeAvailable)
                LockProgress = _gazeWithinSince.HasValue ? Math.Min(1, (nowMs - _gazeWithinSince.Value) / Math.Max(1, _opt.GazeConfirmMs)) : 0;
            else
                LockProgress = Math.Min(1, (nowMs - _startMs) / Math.Max(1, _opt.LockDelayMs));
            if (confirmed || nowMs >= _phaseUntil)
            {
                LockProgress = 1;
                LockEndMs = nowMs;
                LockConfirmedByGaze = confirmed;
                _scheduledOnsetMs = nowMs + _req.PreStimulusDelayMs;
                Current = Phase.Isi;
            }
        }

        /// <summary>Mode B with eye tracking: the lock ended because gaze was confirmed (false = fixed delay or timeout).</summary>
        public bool LockConfirmedByGaze { get; private set; }

        private void StartMarker(double nowMs, int interval)
        {
            CurrentInterval = interval;
            Current = Phase.Marker;
            _phaseUntil = nowMs + _req.MarkerMs;
            if (_req.MarkerMs <= 0) StartStimulus(nowMs);
        }

        private void StartStimulus(double nowMs)
        {
            _framesLeft = FramesPlanned;
            Current = Phase.Stimulus;
            if (!ForcedChoice || CurrentInterval == _req.StimulusInterval || (_req.StimulusInterval == 0 && CurrentInterval == 1))
            {
                OnsetMs = nowMs;
                if (_gazeErr.HasValue) GazeErrorAtOnsetDeg = _gazeErr;
            }
            CountStimulusFrame(nowMs);
        }

        private void CountStimulusFrame(double nowMs)
        {
            _framesLeft--;
            if (StimulusVisible)
            {
                FramesShown++;
                if (_gazeErr.HasValue) GazeMaxErrorDeg = Math.Max(GazeMaxErrorDeg ?? 0, _gazeErr.Value);
            }
        }

        private void EndStimulus(double nowMs)
        {
            if (ForcedChoice && CurrentInterval < _req.Intervals)
            {
                Current = Phase.Gap;
                _phaseUntil = nowMs + _req.IntervalGapMs;
                if (_req.IntervalGapMs <= 0) StartMarker(nowMs, CurrentInterval + 1);
                return;
            }
            Current = Phase.Window;
            _windowStartMs = ForcedChoice ? nowMs : OnsetMs.Value;
            CheckWindow(nowMs);
        }

        private void CheckWindow(double nowMs)
        {
            if (ResponseMs.HasValue || nowMs >= _windowStartMs + _req.ResponseWindowMs) Current = Phase.Done;
        }

        /// <summary>Button pressed (down edge) at <paramref name="tMs"/>.</summary>
        public void Press(double tMs)
        {
            if (Current == Phase.Done) return;
            if (ForcedChoice)
            {
                if (CurrentInterval == 0) { EarlyPresses++; return; }
                if (ResponseMs.HasValue) return;
                if (Current == Phase.Window && tMs > _windowStartMs + _req.ResponseWindowMs) return;
                ResponseMs = tMs;
                RespondedInterval = CurrentInterval;
                return;
            }
            if (!OnsetMs.HasValue || tMs < OnsetMs.Value) { EarlyPresses++; return; }
            if (ResponseMs.HasValue) return;
            if (tMs <= OnsetMs.Value + _req.ResponseWindowMs) ResponseMs = tMs;
        }

        /// <summary>Result for <see cref="ExamSession.Submit"/>. <paramref name="examStartMs"/> converts onset to exam time.</summary>
        public PresentationResult Result(double examStartMs)
        {
            if (!IsDone) throw new InvalidOperationException("presentation not finished");
            var r = new PresentationResult
            {
                Sequence = _req.Sequence,
                Responded = ResponseMs.HasValue,
                LatencyMs = ResponseMs.HasValue && ResponseMs.Value >= OnsetMs.Value ? ResponseMs.Value - OnsetMs.Value : (double?)null,
                OnsetMs = OnsetMs.Value - examStartMs,
                FixationOk = true,
                RespondedInterval = RespondedInterval,
            };
            FillGaze(r);
            return r;
        }

        /// <summary>Result for a presentation interrupted by a pause; ignored by the strategy.</summary>
        public PresentationResult Aborted(double examStartMs, double nowMs)
        {
            var r = new PresentationResult
            {
                Sequence = _req.Sequence,
                Responded = ResponseMs.HasValue,
                LatencyMs = ResponseMs.HasValue && OnsetMs.HasValue && ResponseMs.Value >= OnsetMs.Value ? ResponseMs.Value - OnsetMs.Value : (double?)null,
                OnsetMs = (OnsetMs ?? nowMs) - examStartMs,
                RespondedInterval = RespondedInterval,
                Discarded = true,
            };
            FillGaze(r);
            return r;
        }

        private void FillGaze(PresentationResult r)
        {
            r.GazeTracked = GazeTracked;
            r.GazeErrorDeg = GazeErrorAtOnsetDeg;
            r.GazeMaxErrorDeg = GazeMaxErrorDeg;
        }
    }
}
