using System;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    public class FlowTests
    {
        private static PatientFlow NewFlow(MemorySink sink, ExamOrder order = null) =>
            new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                order ?? new ExamOrder { PresetId = "24-2-zest", AgeYears = 60, PatientRef = "P-001", Seed = 11 });

        [Test]
        public void CompleteFlow_BothEyes_24_2_Zest_Monocular()
        {
            var sink = new MemorySink();
            var flow = NewFlow(sink);
            var d = new FlowDriver(flow, 21);
            bool otherEyeEverLit = false;
            var seenStates = new System.Collections.Generic.HashSet<FlowState>();
            d.RunUntil(() =>
            {
                seenStates.Add(flow.State);
                var v = flow.View;
                if (v.Stimulus != null && v.MonocularEye != v.Stimulus.Eye) otherEyeEverLit = true;
                if (flow.State == FlowState.Exam) Assert.That(v.MonocularEye, Is.EqualTo(flow.CurrentEye));
                if (flow.State == FlowState.Exam) Assert.That(v.Message, Is.Null, "no text during the exam");
                return flow.IsFinished;
            });

            Assert.That(flow.State, Is.EqualTo(FlowState.Done));
            Assert.That(flow.View.Message, Is.EqualTo("Exame concluído. Aguarde o médico."));
            Assert.That(otherEyeEverLit, Is.False);
            Assert.That(seenStates, Is.SupersetOf(new[] { FlowState.Welcome, FlowState.Instructions, FlowState.EyeIntro, FlowState.SharpnessCheck,
                FlowState.TrainingTrial, FlowState.TrainingFeedback, FlowState.ExamReady,
                FlowState.Exam, FlowState.BetweenEyes }));
            Assert.That(seenStates, Does.Not.Contain(FlowState.SetupPreset), "the exam order supplied preset and age");
            Assert.That(seenStates, Does.Not.Contain(FlowState.OcclusionIntro), "occlusion check is off by default");

            Assert.That(sink.Exams.Select(e => e.Settings.Eye).ToList(), Is.EqualTo(new[] { Eye.OD, Eye.OS }));
            Assert.That(sink.Logs, Has.Count.EqualTo(1));
            var log = sink.Logs[0];
            Assert.That(log.Outcome, Is.EqualTo("completed"));
            Assert.That(log.Training.Count(t => t.Eye == Eye.OD), Is.EqualTo(Env.Headset.Training.Trials.Count), "training once, on the first eye");
            Assert.That(log.Training.All(t => t.Correct));
            Assert.That(log.ConfigHash, Is.EqualTo(Env.Config.Hash));

            foreach (var exam in sink.Exams)
            {
                Assert.That(exam.Aborted, Is.False);
                Assert.That(exam.Settings.Strategy, Is.EqualTo(StrategyKind.Zest));
                Assert.That(exam.Settings.PatientRef, Is.EqualTo("P-001"));
                Assert.That(exam.Settings.AgeYears, Is.EqualTo(60));
                Assert.That(exam.Calibrated, Is.False);
                Assert.That(exam.Reliability.BlindSpotFound, "Heijl-Krakau: blind spot mapped");
                Assert.That(exam.Reliability.FixationLosses.Trials, Is.GreaterThan(0));
                Assert.That(exam.Presentations.All(p => p.Request.Kind == PresentationKind.FalsePositiveCatch || p.Result.OnsetMs >= 0));
                Assert.That(exam.Locations.Where(l => !l.IsBlindSpot).All(l => l.StopReason != "NotStarted"));

                // accuracy against the simulated patient's true field (same acceptance criterion as /core)
                var field = d.Fields[exam.Settings.Eye];
                var scale = Env.Lum.ScaleFor(exam.Settings.Eye);
                var errs = exam.Locations.Where(l => !l.IsBlindSpot)
                    .Select(l => Math.Abs(scale.ToHfaEquivalent(l.ThresholdDb) - Math.Max(field.TrueHfaDb[l.Index], scale.ToHfaEquivalent(0))))
                    .ToList();
                Assert.That(errs.Average(), Is.LessThan(2.0), "MAE (dB)");

                var a = FieldAnalysis.Analyze(exam, Env.Config, ProvisionalNormative.For(Env.Config, GridLibrary.Get(Env.Config, "24-2")));
                Assert.That(a, Is.Not.Null);
            }
            // 200 ms at 90 Hz = 18 frames
            Assert.That(d.MaxStimulusFramesSeen, Is.EqualTo(18));
        }

        [Test]
        public void WithoutOrder_OperatorChoosesPresetAndAge()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink, new ExamOrder { Seed = 3 });
            var d = new FlowDriver(flow, 3);
            d.RunUntil(() => flow.State == FlowState.SetupPreset);
            Assert.That(flow.View.Message, Does.Contain("24-2 ZEST (padrão)"));
            d.Press(); d.RunUntil(() => flow.Preset.Id == "24-2-zest-fast");
            flow.OperatorAdjust(+1);
            Assert.That(flow.Preset.Id, Is.EqualTo("24-2-zest-od"));
            flow.OperatorAdjust(-1); flow.OperatorAdjust(-1); flow.OperatorAdjust(-1);
            Assert.That(flow.Preset.Id, Is.EqualTo("24-2-zest-os"), "wraps around");
            d.Press(1700); d.RunUntil(() => flow.State == FlowState.SetupAge);
            Assert.That(flow.View.Message, Does.Contain("50 anos"));
            flow.OperatorAdjust(+7);
            d.Press(1700); d.RunUntil(() => flow.State == FlowState.Instructions);
            Assert.That(flow.AgeYears, Is.EqualTo(57));
            d.RunUntil(() => flow.IsFinished);
            Assert.That(sink.Exams.Single().Settings.Eye, Is.EqualTo(Eye.OS));
            Assert.That(sink.Exams.Single().Settings.AgeYears, Is.EqualTo(57));
            Assert.That(sink.Logs[0].PresetId, Is.EqualTo("24-2-zest-os"));
        }

        [Test]
        public void HoldingButton_PausesExam_DiscardsPresentation_AndResumes()
        {
            var sink = new MemorySink();
            var flow = NewFlow(sink, new ExamOrder { PresetId = "24-2-zest-fast", AgeYears = 45, Seed = 5 });
            var d = new FlowDriver(flow, 5);
            d.RunUntil(() => flow.State == FlowState.Exam && flow.CurrentSession.BuildRecord().Presentations.Count > 20
                && !d.Busy && flow.CurrentPresentation?.Current == PresentationTimeline.Phase.Stimulus);
            d.Press(2000);
            d.RunUntil(() => flow.State == FlowState.Paused);
            var rec = flow.CurrentSession.BuildRecord();
            Assert.That(rec.Presentations.Last().Disposition, Is.EqualTo("aborted"));
            Assert.That(flow.View.MessageKey, Is.EqualTo("pause"));
            Assert.That(flow.View.Stimulus, Is.Null);
            // released hold must not resume by itself
            for (int i = 0; i < 30; i++) d.Frame();
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused).Or.EqualTo(FlowState.Resuming));
            d.RunUntil(() => flow.State == FlowState.Exam);
            d.RunUntil(() => flow.IsFinished);
            var exam = sink.Exams[0];
            Assert.That(exam.Reliability.Pauses, Has.Count.GreaterThanOrEqualTo(1));
            Assert.That(exam.Reliability.Pauses[0].Reason, Is.EqualTo("patient"));
            Assert.That(exam.Reliability.PausedMs, Is.GreaterThan(Env.Config.Timing.ResumeAfterPauseDelayMs));
            Assert.That(exam.Reliability.AbortedPresentations, Is.GreaterThanOrEqualTo(1));
        }

        [Test]
        public void RemovingHeadset_PausesUntilPutBackAndPressed()
        {
            var sink = new MemorySink();
            var flow = NewFlow(sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 45, Seed = 9 });
            var d = new FlowDriver(flow, 9);
            d.RunUntil(() => flow.State == FlowState.Exam && flow.CurrentSession.BuildRecord().Presentations.Count > 10);
            flow.SetUserPresent(false, d.Now);
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused));
            Assert.That(flow.View.MessageKey, Is.EqualTo("pause.headsetRemoved"));
            for (int i = 0; i < 200; i++) d.Frame(); // patient presses while the headset is off: ignored
            Assert.That(flow.State, Is.EqualTo(FlowState.Paused));
            flow.SetUserPresent(true, d.Now);
            d.RunUntil(() => flow.State == FlowState.Exam);
            d.RunUntil(() => flow.IsFinished);
            Assert.That(sink.Exams[0].Reliability.Pauses.Single().Reason, Is.EqualTo("headsetRemoved"));
            Assert.That(sink.Logs[0].Events.Any(e => e.Type == "userPresence" && e.Detail == "off"));
        }

        [Test]
        public void OcclusionCheck_PressMeansFailure_OperatorHoldRetries()
        {
            var sink = new MemorySink();
            var h = Env.Headset;
            h.Checks.OcclusionCheckEnabled = true;
            var flow = new PatientFlow(Env.Config, h, Env.Lum, sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, Seed = 2 });
            var d = new FlowDriver(flow, 2);
            int windows = 0;
            d.PressDuringOcclusion = _ => windows == 1;
            d.RunUntil(() => flow.State == FlowState.OcclusionWindow);
            windows = 1;
            var v = flow.View;
            Assert.That(v.Stimulus, Is.Not.Null, "bright disc shown to the tested eye only");
            Assert.That(v.Stimulus.DiameterDeg, Is.EqualTo(10));
            Assert.That(v.MonocularEye, Is.EqualTo(Eye.OD));
            d.RunUntil(() => flow.State == FlowState.OcclusionFailed);
            Assert.That(flow.View.MonocularEye, Is.Null, "failure message visible to both eyes");
            windows = 2;
            for (int i = 0; i < 300; i++) d.Frame(); // taps do nothing
            Assert.That(flow.State, Is.EqualTo(FlowState.OcclusionFailed));
            d.Press(1700);
            d.RunUntil(() => flow.State == FlowState.OcclusionIntro);
            d.RunUntil(() => flow.State == FlowState.TrainingIntro);
            var events = flow.Log.Events.Where(e => e.Type == "occlusionCheck").Select(e => e.Detail).ToList();
            Assert.That(events[0], Does.StartWith("failed"));
            Assert.That(events.Last(), Is.EqualTo("passed"));
        }

        [Test]
        public void Training_GivesFeedback_AndRepeatsWhenPatientPressesForEverything()
        {
            var sink = new MemorySink();
            var h = Env.Headset;
            h.Training.RepeatIfMissedMoreThan = 0;
            var flow = new PatientFlow(Env.Config, h, Env.Lum, sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, Seed = 4 });
            var d = new FlowDriver(flow, 4) { TrainingAlwaysPress = true };
            var keys = new System.Collections.Generic.List<string>();
            d.RunUntil(() =>
            {
                if (flow.State == FlowState.TrainingFeedback && (keys.Count == 0 || keys.Last() != flow.View.MessageKey)) keys.Add(flow.View.MessageKey);
                return flow.State == FlowState.ExamReady;
            });
            Assert.That(keys, Does.Contain("training.hit"));
            Assert.That(keys, Does.Contain("training.falseAlarm"));
            Assert.That(flow.Log.Training.Max(t => t.Round), Is.EqualTo(1), "one repeat round (maxRepeats = 1), then the exam");
            Assert.That(flow.Log.Training.Count, Is.EqualTo(2 * h.Training.Trials.Count));
        }

        [Test]
        public void OperatorAbort_SavesPartialExamFlaggedAborted()
        {
            var sink = new MemorySink();
            var flow = NewFlow(sink);
            var d = new FlowDriver(flow, 8);
            d.RunUntil(() => flow.State == FlowState.Exam && flow.CurrentSession.BuildRecord().Presentations.Count > 30);
            flow.OperatorAbort(d.Now);
            Assert.That(flow.State, Is.EqualTo(FlowState.Aborted));
            Assert.That(flow.View.Message, Is.EqualTo("Exame interrompido. Aguarde o médico."));
            var exam = sink.Exams.Single();
            Assert.That(exam.Aborted);
            Assert.That(sink.Logs.Single().Outcome, Is.EqualTo("aborted"));
            Assert.Throws<InvalidOperationException>(() => FieldAnalysis.Analyze(exam, Env.Config, ProvisionalNormative.For(Env.Config, GridLibrary.Get(Env.Config, "24-2"))));
            flow.OperatorAbort(d.Now); // idempotent
            Assert.That(sink.Logs, Has.Count.EqualTo(1));
        }

        [Test]
        public void SaveFailure_ShowsErrorScreen()
        {
            var sink = new MemorySink { Fail = true };
            var flow = NewFlow(sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 70, Seed = 1 });
            var d = new FlowDriver(flow, 1);
            d.RunUntil(() => flow.IsFinished);
            Assert.That(flow.State, Is.EqualTo(FlowState.SaveError));
            Assert.That(flow.View.MessageKey, Is.EqualTo("error.save"));
            Assert.That(flow.CompletedExams, Has.Count.EqualTo(1), "the record is still kept in memory");
        }

        [Test]
        public void OcclusionCheck_IsOffByDefault_AndSkipped()
        {
            var h = Env.Headset;
            Assert.That(h.Checks.OcclusionCheckEnabled, Is.False);
            var flow = new PatientFlow(Env.Config, h, Env.Lum, new MemorySink(), new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, Seed = 12 });
            var d = new FlowDriver(flow, 12);
            bool sawOcclusion = false;
            d.RunUntil(() => { sawOcclusion |= flow.State == FlowState.OcclusionIntro; return flow.State == FlowState.TrainingIntro; });
            Assert.That(sawOcclusion, Is.False);
        }

        [Test]
        public void AudioCue_IsDeliveredOncePerMessage()
        {
            var flow = NewFlow(new MemorySink());
            var d = new FlowDriver(flow, 1);
            d.Frame();
            Assert.That(flow.View.AudioCue, Is.EqualTo("welcome"));
            d.Frame();
            Assert.That(flow.View.AudioCue, Is.Null);
            d.RunUntil(() => flow.State == FlowState.Instructions);
            Assert.That(flow.View.AudioCue, Is.EqualTo("instructions"));
        }

        [Test]
        public void UnknownPreset_InOrder_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => NewFlow(new MemorySink(), new ExamOrder { PresetId = "nope" }));
        }
    
        [Test]
        public void MeasuredRefreshRate_SetsStimulusFrames_AndIsLogged()
        {
            var sink = new MemorySink();
            var flow = NewFlow(sink, new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 50, Seed = 6 });
            flow.SetMeasuredRefreshRate(72);
            flow.Note("eyePositions", "xr ipd=63.0mm");
            Assert.Throws<ArgumentOutOfRangeException>(() => flow.SetMeasuredRefreshRate(0));
            var d = new FlowDriver(flow, 6);
            d.RunUntil(() => flow.State == FlowState.Exam && flow.CurrentPresentation != null);
            Assert.That(flow.CurrentPresentation.FramesPlanned, Is.EqualTo(14), "200 ms at 72 Hz");
            flow.SetMeasuredRefreshRate(120); // ignored during the exam
            Assert.That(flow.RefreshRateHz, Is.EqualTo(72));
            Assert.That(flow.Log.RefreshRateHz, Is.EqualTo(72));
            Assert.That(flow.Log.Events.Any(e => e.Type == "refreshRate" && e.Detail.Contains("differs")));
            Assert.That(flow.Log.Events.Any(e => e.Type == "eyePositions"));
        }
    }
}
