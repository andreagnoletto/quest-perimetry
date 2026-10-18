using System;
using Perimetry.Core.Exam;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Timing of one presentation, driven once per rendered frame. Times are the predicted display time
    /// of each frame (ms, monotonic). Sequence: pre-stimulus interval (ISI) -> stimulus for a whole number
    /// of frames (duration quantised to the refresh rate) -> response window counted from onset.
    /// A press counts only if it comes at or after onset and within the window; earlier presses are counted
    /// in <see cref="EarlyPresses"/> and logged. The presentation ends at the first valid press (once the
    /// stimulus has finished) or when the window elapses.
    /// </summary>
    public sealed class PresentationTimeline
    {
        public enum Phase { Isi, Stimulus, Window, Done }

        private readonly PresentationRequest _req;
        private readonly double _scheduledOnsetMs;
        private int _framesLeft;

        public PresentationTimeline(PresentationRequest request, double startMs, double refreshRateHz)
        {
            _req = request ?? throw new ArgumentNullException(nameof(request));
            if (refreshRateHz <= 0) throw new ArgumentOutOfRangeException(nameof(refreshRateHz));
            _scheduledOnsetMs = startMs + request.PreStimulusDelayMs;
            FramesPlanned = Math.Max(1, (int)Math.Round(request.DurationMs * refreshRateHz / 1000.0));
            _framesLeft = FramesPlanned;
        }

        public PresentationRequest Request => _req;
        public Phase Current { get; private set; } = Phase.Isi;
        /// <summary>True on frames where the stimulus must be drawn (never for a blank catch trial).</summary>
        public bool StimulusVisible => Current == Phase.Stimulus && _req.StimulusShown;
        public int FramesPlanned { get; }
        public int FramesShown { get; private set; }
        public double? OnsetMs { get; private set; }
        public double? ResponseMs { get; private set; }
        public int EarlyPresses { get; private set; }
        public bool IsDone => Current == Phase.Done;

        public void Tick(double nowMs)
        {
            switch (Current)
            {
                case Phase.Isi:
                    if (nowMs >= _scheduledOnsetMs)
                    {
                        OnsetMs = nowMs;
                        Current = Phase.Stimulus;
                        CountStimulusFrame();
                    }
                    break;
                case Phase.Stimulus:
                    if (_framesLeft > 0) CountStimulusFrame();
                    else Current = Phase.Window;
                    if (Current == Phase.Window) CheckWindow(nowMs);
                    break;
                case Phase.Window:
                    CheckWindow(nowMs);
                    break;
            }
        }

        private void CountStimulusFrame()
        {
            _framesLeft--;
            if (_req.StimulusShown) FramesShown++;
        }

        private void CheckWindow(double nowMs)
        {
            if (ResponseMs.HasValue || nowMs >= OnsetMs.Value + _req.ResponseWindowMs) Current = Phase.Done;
        }

        /// <summary>Button pressed (down edge) at <paramref name="tMs"/>.</summary>
        public void Press(double tMs)
        {
            if (Current == Phase.Done) return;
            if (!OnsetMs.HasValue || tMs < OnsetMs.Value) { EarlyPresses++; return; }
            if (ResponseMs.HasValue) return;
            if (tMs <= OnsetMs.Value + _req.ResponseWindowMs) ResponseMs = tMs;
        }

        /// <summary>Result for <see cref="ExamSession.Submit"/>. <paramref name="examStartMs"/> converts onset to exam time.</summary>
        public PresentationResult Result(double examStartMs)
        {
            if (!IsDone) throw new InvalidOperationException("presentation not finished");
            return new PresentationResult
            {
                Sequence = _req.Sequence,
                Responded = ResponseMs.HasValue,
                LatencyMs = ResponseMs.HasValue ? ResponseMs.Value - OnsetMs.Value : (double?)null,
                OnsetMs = OnsetMs.Value - examStartMs,
                FixationOk = true,
            };
        }

        /// <summary>Result for a presentation interrupted by a pause; ignored by the strategy.</summary>
        public PresentationResult Aborted(double examStartMs, double nowMs) => new PresentationResult
        {
            Sequence = _req.Sequence,
            Responded = ResponseMs.HasValue,
            LatencyMs = ResponseMs.HasValue && OnsetMs.HasValue ? ResponseMs.Value - OnsetMs.Value : (double?)null,
            OnsetMs = (OnsetMs ?? nowMs) - examStartMs,
            Discarded = true,
        };
    }
}
