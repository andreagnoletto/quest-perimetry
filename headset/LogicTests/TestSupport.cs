using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    internal static class Env
    {
        private static ClinicalConfig _cfg;
        public static ClinicalConfig Config => _cfg ??= ClinicalConfig.LoadFromDirectory(ClinicalConfig.FindConfigDirectory(TestContext.CurrentContext.TestDirectory));
        public static HeadsetConfig Headset => HeadsetConfig.From(Config);
        public static LuminanceMapper Lum => new LuminanceMapper(Config.Display);
    }

    internal sealed class MemorySink : IExamSink
    {
        public readonly List<ExamRecord> Exams = new List<ExamRecord>();
        public readonly List<SessionLog> Logs = new List<SessionLog>();
        public bool Fail;
        public void SaveExam(ExamRecord exam) { if (Fail) throw new System.IO.IOException("disk full"); Exams.Add(exam); }
        public void SaveSessionLog(SessionLog log) { if (Fail) throw new System.IO.IOException("disk full"); Logs.Add(log); }
    }

    /// <summary>
    /// Drives a <see cref="PatientFlow"/> frame by frame like the Unity layer does, with a simulated patient:
    /// it taps through screens and answers stimuli through <see cref="SimulatedObserver"/> (per eye).
    /// </summary>
    internal sealed class FlowDriver
    {
        public const double Hz = 90;
        public double Now;
        public readonly PatientFlow Flow;
        public readonly Dictionary<Eye, SimulatedObserver> Observers = new Dictionary<Eye, SimulatedObserver>();
        public readonly Dictionary<Eye, SyntheticField> Fields = new Dictionary<Eye, SyntheticField>();
        public Func<FlowState, bool> PressDuringOcclusion = _ => false;
        public bool TrainingAlwaysPress;
        public int MaxStimulusFramesSeen;

        private readonly Rng _rng;
        private double? _pressAt, _releaseAt;
        private PresentationTimeline _answered;
        private FlowState _lastState;
        private double _stateSince;

        public FlowDriver(PatientFlow flow, ulong seed, DefectType defect = DefectType.Normal, double age = 60)
        {
            Flow = flow;
            _rng = new Rng(seed);
            var grid = GridLibrary.Get(Env.Config, flow.Preset.GridId);
            foreach (Eye e in new[] { Eye.OD, Eye.OS })
            {
                var field = SyntheticField.Create(Env.Config.ProvisionalNormative, grid, age, defect, 15, _rng.Fork());
                Fields[e] = field;
                Observers[e] = new SimulatedObserver(field, Env.Lum.ScaleFor(e), e, new ObserverParameters(), _rng.Fork());
            }
            Now = 1000;
            _lastState = flow.State;
        }

        /// <summary>Starts a press now (after any press in progress is released).</summary>
        public void Press(double holdMs = 120)
        {
            if (Busy) RunUntil(() => !Busy);
            _pressAt = Now; _releaseAt = Now + holdMs;
        }

        /// <summary>One rendered frame: input edges first, then Tick (same order as PerimetryApp).</summary>
        public void Frame()
        {
            Now += 1000.0 / Hz;
            if (_pressAt.HasValue && Now >= _pressAt.Value) { Flow.ButtonDown(Now); _pressAt = null; }
            if (_releaseAt.HasValue && _pressAt == null && Now >= _releaseAt.Value) { Flow.ButtonUp(Now); _releaseAt = null; }
            Flow.Tick(Now);
            if (Flow.State != _lastState) { _lastState = Flow.State; _stateSince = Now; }
            Patient();
        }

        public bool Busy => _pressAt.HasValue || _releaseAt.HasValue;

        private void Patient()
        {
            var s = Flow.State;
            var tl = Flow.CurrentPresentation;
            if ((s == FlowState.Exam || s == FlowState.TrainingTrial) && tl != null && tl.OnsetMs.HasValue && tl != _answered)
            {
                _answered = tl;
                var eye = Flow.CurrentEye.Value;
                bool respond; double latency;
                if (s == FlowState.TrainingTrial)
                {
                    respond = TrainingAlwaysPress || tl.Request.StimulusShown;
                    latency = 420;
                }
                else
                {
                    var r = Observers[eye].Respond(tl.Request, 0);
                    respond = r.Responded; latency = r.LatencyMs ?? 0;
                }
                if (respond) { _pressAt = tl.OnsetMs.Value + latency; _releaseAt = _pressAt + 120; }
                return;
            }
            if (Flow.View.Stimulus != null && s == FlowState.Exam) MaxStimulusFramesSeen = Math.Max(MaxStimulusFramesSeen, tl.FramesShown);
            if (Busy) return;
            if (s == FlowState.OcclusionWindow)
            {
                if (PressDuringOcclusion(s)) Press();
                return;
            }
            bool screen = s == FlowState.Welcome || s == FlowState.Instructions || s == FlowState.EyeIntro || s == FlowState.SharpnessCheck
                || s == FlowState.OcclusionIntro || s == FlowState.TrainingIntro || s == FlowState.TrainingRepeat
                || s == FlowState.ExamReady || s == FlowState.BetweenEyes || s == FlowState.Paused;
            if (screen && Now - _stateSince > 600) Press();
        }

        public void RunUntil(Func<bool> cond, double maxMs = 30 * 60 * 1000)
        {
            double end = Now + maxMs;
            while (!cond())
            {
                if (Now > end) Assert.Fail($"timed out in state {Flow.State}");
                Frame();
            }
        }
    }
}
