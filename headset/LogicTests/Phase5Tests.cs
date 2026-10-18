using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Strategies;
using Perimetry.Headset.Logic;

namespace Perimetry.Headset.Logic.Tests
{
    /// <summary>Phase 5 on the headset: presets, Diamond4 target, oculokinetic mode B, gaze, forced choice.</summary>
    public class Phase5Tests
    {
        private static PresentationRequest Req(int isi = 800, double tx = 0, double ty = 0, double fx = 9, double fy = 9) => new PresentationRequest
        {
            Sequence = 3, Kind = PresentationKind.Threshold, StimulusShown = true, DurationMs = 200, ResponseWindowMs = 1000, PreStimulusDelayMs = isi,
            TargetX = tx, TargetY = ty, FieldX = fx, FieldY = fy, SizeDeg = 0.431, LuminanceCdm2 = 20,
        };

        private static PresentationRequest ForcedChoiceReq(int stimulusInterval, int isi = 700) => new PresentationRequest
        {
            Sequence = 4, Kind = PresentationKind.Threshold, StimulusShown = true, DurationMs = 200, ResponseWindowMs = 1000, PreStimulusDelayMs = isi,
            Intervals = 2, StimulusInterval = stimulusInterval, IntervalMs = 200, IntervalGapMs = 600, MarkerMs = 100,
        };

        // ------------------------------------------------------------------ presets and config

        [Test]
        public void Presets_CoverAllGrids_ReferenceKnownGridsAndStrategies_DefaultsUnchanged()
        {
            var h = Env.Headset;
            Assert.That(h.Presets.Select(p => p.GridId).Distinct(), Is.EquivalentTo(new[] { "24-2", "30-2", "10-2", "24-2C" }));
            Assert.That(h.Presets.All(p => Env.Config.Grids.ContainsKey(p.GridId)), "every preset names a grid in config/grids");
            Assert.That(h.Presets.Select(p => p.Id).Distinct().Count(), Is.EqualTo(h.Presets.Count), "preset ids are unique");
            foreach (var p in h.Presets) GridLibrary.Get(Env.Config, p.GridId);

            var def = h.Presets[0];
            Assert.That(def.Id, Is.EqualTo("24-2-zest"));
            Assert.That(def.FixationMode, Is.EqualTo(FixationMode.CentralHeijlKrakau));
            Assert.That(def.FovealThreshold, Is.False, "foveal test is off by default (HFA Fovea: OFF)");
            Assert.That(def.ResponseMode, Is.EqualTo(ResponseMode.YesNo));
            Assert.That(h.Presets.Count(p => p.FovealThreshold), Is.EqualTo(1));
            Assert.That(h.Preset("24-2-zest-fast-forced-choice").ResponseMode, Is.EqualTo(ResponseMode.ForcedChoice));
            Assert.That(h.Preset("24-2-zest-fast-oculokinetic").FixationMode, Is.EqualTo(FixationMode.Oculokinetic));
            Assert.That(h.Preset("24-2-zest-fast-eyetracking").FixationMode, Is.EqualTo(FixationMode.CentralEyeTracking));
            Assert.That(h.Preset("30-2-zest").Strategy, Is.EqualTo(StrategyKind.Zest));
            Assert.That(h.Preset("10-2-zest-fast").Strategy, Is.EqualTo(StrategyKind.ZestFast));

            // 30-2 stays inside the usable half field of the display config
            var g30 = GridLibrary.Get(Env.Config, "30-2");
            Assert.That(g30.Locations.Max(l => Math.Max(Math.Abs(l.Point.X), Math.Abs(l.Point.Y))), Is.EqualTo(27));
            Assert.That(g30.Locations.Max(l => AngularGeometry.Eccentricity(l.Point)), Is.LessThan(Env.Config.Display.UsableHalfFieldDeg));

            Assert.That(h.FixationTarget.DiamondDotSizeDeg, Is.EqualTo(0.4));
            Assert.That(h.FixationTarget.LockAnimation.StartScale, Is.EqualTo(1.6));
            Assert.That(h.Oculokinetic.GazeConfirmTimeoutFactor, Is.EqualTo(5));
            Assert.That(h.Oculokinetic.GazeStaleMs, Is.EqualTo(500));
        }

        // ------------------------------------------------------------------ fixation layout

        [Test]
        public void FixationLayout_Central_IsTheTarget_Diamond4_IsFourDotsAroundIt()
        {
            var c = FixationLayout.Points(FixationTargetKind.Central, new FieldPoint(0, 0), 1.5);
            Assert.That(c, Has.Count.EqualTo(1));
            Assert.That(c[0], Is.EqualTo(new FieldPoint(0, 0)));

            var d = FixationLayout.Points(FixationTargetKind.Diamond4, new FieldPoint(0, 0), 1.5);
            Assert.That(d, Has.Count.EqualTo(4));
            Assert.That(d.Select(p => (Math.Round(p.X, 6), Math.Round(p.Y, 6))), Is.EquivalentTo(new[] { (1.5, 0.0), (0.0, 1.5), (-1.5, 0.0), (0.0, -1.5) }));

            // in mode B the dots are composed around the moved target and keep their 1.5° distance to it
            var t = new FieldPoint(8, -6);
            var dm = FixationLayout.Points(FixationTargetKind.Diamond4, t, 1.5);
            foreach (var p in dm) Assert.That(AngularGeometry.AngleBetweenDeg(t, p), Is.EqualTo(1.5).Within(1e-6));

            var a = new LockAnimationConfig { StartScale = 1.6, StartLuminanceFactor = 0.6 };
            Assert.That(FixationLayout.LockScale(0, a), Is.EqualTo(1.6));
            Assert.That(FixationLayout.LockScale(0.5, a), Is.EqualTo(1.3).Within(1e-9));
            Assert.That(FixationLayout.LockScale(1, a), Is.EqualTo(1));
            Assert.That(FixationLayout.LockScale(7, a), Is.EqualTo(1), "clamped");
            Assert.That(FixationLayout.LockLuminanceFactor(0, a), Is.EqualTo(0.6));
            Assert.That(FixationLayout.LockLuminanceFactor(1, a), Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ timeline: mode B

        [Test]
        public void Oculokinetic_WithoutGaze_LockDelayThenIsiThenStimulus_AnimationProgresses()
        {
            var opt = new TimelineOptions { Oculokinetic = true, LockDelayMs = 600, GazeAvailable = false };
            var tl = new PresentationTimeline(Req(isi: 800, tx: 7, ty: -4), 0, 90, opt);
            Assert.That(tl.Current, Is.EqualTo(PresentationTimeline.Phase.Lock));
            Assert.That(tl.LockProgress, Is.EqualTo(0));
            double dt = 1000 / 90.0, t = 0;
            var progress = new List<double>();
            while (tl.Current == PresentationTimeline.Phase.Lock) { t += dt; tl.Tick(t); progress.Add(tl.LockProgress); }
            Assert.That(tl.LockEndMs.Value, Is.GreaterThanOrEqualTo(600).And.LessThan(600 + dt));
            Assert.That(tl.LockConfirmedByGaze, Is.False);
            Assert.That(progress, Is.Ordered.Ascending);
            Assert.That(progress.Last(), Is.EqualTo(1));
            Assert.That(tl.Current, Is.EqualTo(PresentationTimeline.Phase.Isi));
            while (!tl.OnsetMs.HasValue) { t += dt; tl.Tick(t); }
            Assert.That(tl.OnsetMs.Value - tl.LockEndMs.Value, Is.GreaterThanOrEqualTo(800).And.LessThan(800 + dt), "the random ISI follows the lock");
            Assert.That(tl.GazeTracked, Is.False);
            while (!tl.IsDone) { t += dt; tl.Tick(t); }
            var r = tl.Result(0);
            Assert.That(r.GazeTracked, Is.False);
            Assert.That(r.GazeErrorDeg, Is.Null);
        }

        [Test]
        public void Oculokinetic_WithGaze_LockEndsAfterGazeStaysOnTarget_OrTimesOut()
        {
            var opt = new TimelineOptions { Oculokinetic = true, LockDelayMs = 600, GazeAvailable = true, GazeConfirmMs = 300, GazeConfirmDeg = 3, GazeConfirmTimeoutMs = 3000 };
            var tl = new PresentationTimeline(Req(isi: 600), 0, 90, opt);
            double dt = 1000 / 90.0, t = 0;
            // 250 ms away from the target (saccade), then on it
            while (t < 250) { t += dt; tl.Gaze(t, 12); tl.Tick(t); Assert.That(tl.Current, Is.EqualTo(PresentationTimeline.Phase.Lock)); Assert.That(tl.LockProgress, Is.EqualTo(0)); }
            double arrived = t;
            while (tl.Current == PresentationTimeline.Phase.Lock) { t += dt; tl.Gaze(t, 1.2); tl.Tick(t); }
            Assert.That(tl.LockEndMs.Value - arrived, Is.GreaterThanOrEqualTo(300).And.LessThan(300 + 2 * dt));
            Assert.That(tl.LockConfirmedByGaze, Is.True);
            Assert.That(tl.LockProgress, Is.EqualTo(1));

            // a glance away resets the confirmation; never confirmed = timeout
            var tl2 = new PresentationTimeline(Req(isi: 600), 0, 90, opt);
            t = 0; int k = 0;
            while (tl2.Current == PresentationTimeline.Phase.Lock) { t += dt; tl2.Gaze(t, (k++ % 20) < 19 ? 1.0 : 8.0); tl2.Tick(t); }
            Assert.That(tl2.LockEndMs.Value, Is.GreaterThanOrEqualTo(3000).And.LessThan(3000 + dt));
            Assert.That(tl2.LockConfirmedByGaze, Is.False);
        }

        [Test]
        public void Gaze_OnsetErrorAndMaxErrorOverStimulus_AreReported()
        {
            var tl = new PresentationTimeline(Req(isi: 500), 0, 90);
            double dt = 1000 / 90.0, t = 0;
            int frame = 0;
            while (!tl.IsDone)
            {
                t += dt;
                // 0.5° before onset, then rises 0.5° per frame while the stimulus is on, then back to 0.5°
                double err = tl.Current == PresentationTimeline.Phase.Stimulus ? 0.5 + 0.5 * (++frame) : 0.5;
                tl.Gaze(t, err);
                tl.Tick(t);
            }
            var r = tl.Result(0);
            Assert.That(r.GazeTracked, Is.True);
            Assert.That(r.GazeErrorDeg, Is.EqualTo(0.5), "error on the onset frame (sampled before the tick that starts the stimulus)");
            Assert.That(r.GazeMaxErrorDeg, Is.EqualTo(0.5 + 0.5 * 17).Within(1e-9), "largest error over the 18 stimulus frames");

            // samples missing on the onset frame: onset error null, tracked because of later samples
            var tl2 = new PresentationTimeline(Req(isi: 500), 0, 90);
            t = 0;
            while (!tl2.IsDone) { t += dt; tl2.Gaze(t, tl2.Current == PresentationTimeline.Phase.Window ? 2.0 : (double?)null); tl2.Tick(t); }
            var r2 = tl2.Result(0);
            Assert.That(r2.GazeTracked, Is.True);
            Assert.That(r2.GazeErrorDeg, Is.Null);
            Assert.That(r2.GazeMaxErrorDeg, Is.Null);
        }

        // ------------------------------------------------------------------ timeline: forced choice

        [TestCase(1)]
        [TestCase(2)]
        public void ForcedChoice_MarkersIntervalsGap_StimulusOnlyInItsInterval(int stimulusInterval)
        {
            var tl = new PresentationTimeline(ForcedChoiceReq(stimulusInterval), 0, 90);
            Assert.That(tl.ForcedChoice, Is.True);
            double dt = 1000 / 90.0, t = 0;
            var phases = new List<(PresentationTimeline.Phase phase, int interval, bool stim, bool marker, double t)>();
            while (!tl.IsDone) { t += dt; tl.Tick(t); phases.Add((tl.Current, tl.CurrentInterval, tl.StimulusVisible, tl.FixationMarker, t)); }

            var seq = phases.Select(p => (p.phase, p.interval)).Distinct().ToList();
            Assert.That(seq, Is.EqualTo(new[]
            {
                (PresentationTimeline.Phase.Isi, 0), (PresentationTimeline.Phase.Marker, 1), (PresentationTimeline.Phase.Stimulus, 1),
                (PresentationTimeline.Phase.Gap, 1), (PresentationTimeline.Phase.Marker, 2), (PresentationTimeline.Phase.Stimulus, 2),
                (PresentationTimeline.Phase.Window, 2), (PresentationTimeline.Phase.Done, 2),
            }));
            Assert.That(phases.Count(p => p.stim), Is.EqualTo(18), "200 ms of stimulus at 90 Hz, in one interval only");
            Assert.That(phases.Where(p => p.stim).All(p => p.interval == stimulusInterval));
            Assert.That(phases.Count(p => p.marker), Is.InRange(2 * 9 - 2, 2 * 9 + 2), "two markers of 100 ms");
            Assert.That(phases.All(p => !(p.marker && p.stim)));
            double marker1 = phases.First(p => p.phase == PresentationTimeline.Phase.Marker).t;
            Assert.That(marker1, Is.GreaterThanOrEqualTo(700).And.LessThan(700 + dt));
            double gapStart = phases.First(p => p.phase == PresentationTimeline.Phase.Gap).t;
            double marker2 = phases.First(p => p.phase == PresentationTimeline.Phase.Marker && p.interval == 2).t;
            Assert.That(marker2 - gapStart, Is.GreaterThanOrEqualTo(600).And.LessThan(600 + 2 * dt));
            double lastStimEnd = phases.Last(p => p.phase == PresentationTimeline.Phase.Stimulus).t;
            Assert.That(t - lastStimEnd, Is.GreaterThanOrEqualTo(1000 - dt).And.LessThan(1000 + 2 * dt), "window after the last interval");
            Assert.That(tl.OnsetMs.Value, Is.EqualTo(phases.First(p => p.stim).t).Within(1e-9));

            var r = tl.Result(0);
            Assert.That(r.Responded, Is.False);
            Assert.That(r.RespondedInterval, Is.Null);
        }

        [Test]
        public void ForcedChoice_PressIsAttributedToTheRunningInterval()
        {
            // press during interval 1 (its gap): RespondedInterval = 1
            var tl = new PresentationTimeline(ForcedChoiceReq(2), 0, 90);
            double dt = 1000 / 90.0, t = 0;
            while (tl.Current != PresentationTimeline.Phase.Gap) { t += dt; tl.Tick(t); }
            tl.Press(t + 50);
            while (!tl.IsDone) { t += dt; tl.Tick(t); }
            Assert.That(tl.RespondedInterval, Is.EqualTo(1));
            Assert.That(tl.Current, Is.EqualTo(PresentationTimeline.Phase.Done));
            var r = tl.Result(0);
            Assert.That(r.Responded, Is.True);
            Assert.That(r.RespondedInterval, Is.EqualTo(1));
            Assert.That(r.LatencyMs, Is.Null, "pressed before the stimulus interval: no latency");

            // press in the response window after interval 2: RespondedInterval = 2, latency from the stimulus onset
            var tl2 = new PresentationTimeline(ForcedChoiceReq(2), 0, 90);
            t = 0;
            while (tl2.Current != PresentationTimeline.Phase.Window) { t += dt; tl2.Tick(t); }
            tl2.Press(t + 300);
            tl2.Tick(t + 300 + dt);
            Assert.That(tl2.IsDone);
            var r2 = tl2.Result(0);
            Assert.That(r2.RespondedInterval, Is.EqualTo(2));
            Assert.That(r2.LatencyMs, Is.EqualTo(t + 300 - tl2.OnsetMs.Value).Within(1e-9));

            // press before the first marker is early and ignored; only the first press counts
            var tl3 = new PresentationTimeline(ForcedChoiceReq(1), 0, 90);
            tl3.Press(100);
            Assert.That(tl3.EarlyPresses, Is.EqualTo(1));
            t = 0;
            while (tl3.Current != PresentationTimeline.Phase.Marker) { t += dt; tl3.Tick(t); }
            tl3.Press(t + 10);
            while (tl3.CurrentInterval != 2) { t += dt; tl3.Tick(t); }
            tl3.Press(t + 10);
            while (!tl3.IsDone) { t += dt; tl3.Tick(t); }
            Assert.That(tl3.RespondedInterval, Is.EqualTo(1));
            Assert.That(tl3.Result(0).RespondedInterval, Is.EqualTo(1));
        }

        // ------------------------------------------------------------------ flow

        [Test]
        public void Flow_FovealPreset_RunsTheFovealTestFirst_WithTheDiamondTarget()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-fast-foveal", AgeYears = 55, PatientRef = "P-FOV", Seed = 5 });
            var d = new FlowDriver(flow, 7);
            d.RunUntil(() => flow.State == FlowState.ExamReady);
            Assert.That(flow.View.FixationTarget, Is.EqualTo(FixationTargetKind.Diamond4), "the diamond is shown while the patient reads about it");
            Assert.That(flow.View.Message, Does.Contain("quatro pontos"));

            var kinds = new List<PresentationKind>();
            var targetsByKind = new Dictionary<PresentationKind, HashSet<FixationTargetKind>>();
            PresentationTimeline last = null;
            d.RunUntil(() =>
            {
                var tl = flow.CurrentPresentation;
                if (flow.State == FlowState.Exam && tl != null && tl != last)
                {
                    last = tl;
                    kinds.Add(tl.Request.Kind);
                    if (!targetsByKind.TryGetValue(tl.Request.Kind, out var set)) targetsByKind[tl.Request.Kind] = set = new HashSet<FixationTargetKind>();
                    set.Add(flow.View.FixationTarget);
                    if (tl.Request.Kind == PresentationKind.Foveal)
                    {
                        Assert.That(flow.View.DiamondHalfSizeDeg, Is.EqualTo(Env.Config.Strategies.Foveal.DiamondHalfSizeDeg));
                        Assert.That(flow.View.FixationX, Is.EqualTo(0));
                    }
                }
                return flow.State == FlowState.BetweenEyes || flow.IsFinished;
            });
            int firstNonFoveal = kinds.FindIndex(k => k != PresentationKind.Foveal);
            Assert.That(firstNonFoveal, Is.GreaterThan(0), "foveal presentations come first");
            Assert.That(kinds.Skip(firstNonFoveal).All(k => k != PresentationKind.Foveal), "and never again");
            Assert.That(targetsByKind[PresentationKind.Foveal], Is.EquivalentTo(new[] { FixationTargetKind.Diamond4 }));
            Assert.That(targetsByKind[PresentationKind.Threshold], Is.EquivalentTo(new[] { FixationTargetKind.Central }));

            var exam = sink.Exams[0];
            Assert.That(exam.Settings.FovealThreshold, Is.True);
            Assert.That(exam.Foveal, Is.Not.Null);
            Assert.That(exam.Foveal.Presentations, Is.GreaterThan(0));
            Assert.That(double.IsNaN(exam.Foveal.ThresholdDb), Is.False);
            Assert.That(exam.Presentations.Count(p => p.Request.Kind == PresentationKind.Foveal), Is.EqualTo(exam.Foveal.Presentations));
            Assert.That(exam.Locations.All(l => !(l.X == 0 && l.Y == 0)), "the fovea is not a grid location");
        }

        [Test]
        public void Flow_OculokineticPreset_PlacesStimuliAroundTheMovingTarget_AndCompletes()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-fast-oculokinetic", AgeYears = 50, PatientRef = "P-OK", Seed = 9 });
            var d = new FlowDriver(flow, 13);
            d.RunUntil(() => flow.State == FlowState.Instructions);
            Assert.That(flow.View.Message, Does.Contain("mudar de lugar"));

            var targets = new HashSet<(double, double)>();
            int framesChecked = 0;
            double range = Env.Config.Reliability.Oculokinetic.TargetRangeDeg, maxEcc = Env.Config.Reliability.Oculokinetic.MaxEccentricityDeg;
            d.RunUntil(() =>
            {
                var v = flow.View;
                var tl = flow.CurrentPresentation;
                if (flow.State == FlowState.Exam && tl != null && v.Stimulus != null && tl.Request.Kind != PresentationKind.Foveal)
                {
                    framesChecked++;
                    var r = tl.Request;
                    targets.Add((r.TargetX, r.TargetY));
                    Assert.That(Math.Abs(r.TargetX), Is.LessThanOrEqualTo(range));
                    Assert.That(Math.Abs(r.TargetY), Is.LessThanOrEqualTo(range));
                    Assert.That(v.FixationX, Is.EqualTo(r.TargetX)); Assert.That(v.FixationY, Is.EqualTo(r.TargetY));
                    var fix = new FieldPoint(v.FixationX, v.FixationY);
                    var stim = new FieldPoint(v.Stimulus.FieldX, v.Stimulus.FieldY);
                    // drawn stimulus is the composition: its angular distance to the target is the retinal eccentricity
                    Assert.That(AngularGeometry.AngleBetweenDeg(fix, stim), Is.EqualTo(AngularGeometry.Eccentricity(new FieldPoint(r.FieldX, r.FieldY))).Within(1e-6));
                    Assert.That(AngularGeometry.Eccentricity(stim), Is.LessThanOrEqualTo(maxEcc + 1e-9));
                    Assert.That(tl.LockProgress, Is.EqualTo(1), "stimulus only after the lock");
                    Assert.That(v.FixationScale, Is.EqualTo(1));
                }
                if (flow.State == FlowState.Exam && tl != null && tl.Current == PresentationTimeline.Phase.Lock)
                    Assert.That(v.FixationScale, Is.GreaterThan(1).Or.EqualTo(1), "lock animation shrinks to 1");
                return flow.State == FlowState.BetweenEyes || flow.IsFinished;
            });
            Assert.That(framesChecked, Is.GreaterThan(1000));
            Assert.That(targets.Count, Is.GreaterThan(50), "the target moves to a new position before each presentation");

            var exam = sink.Exams[0];
            Assert.That(exam.Settings.FixationMode, Is.EqualTo(FixationMode.Oculokinetic));
            Assert.That(exam.Aborted, Is.False);
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotCatch), "Heijl-Krakau stays on in mode B by default");
            Assert.That(exam.Presentations.Where(p => p.Request.Kind != PresentationKind.Foveal).Select(p => (p.Request.TargetX, p.Request.TargetY)).Distinct().Count(), Is.GreaterThan(50));
            Assert.That(sink.Logs.Count == 0 || sink.Logs[0].Events.Any(e => e.Type == "examStart" && e.Detail.Contains("Oculokinetic")));
        }

        [Test]
        public void Flow_EyeTrackingPreset_RejectsAndRetestsOffTargetPresentations_AndRecordsTheTrace()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-fast-eyetracking", AgeYears = 50, PatientRef = "P-GZ", Seed = 21 });
            var d = new FlowDriver(flow, 17);
            // Simulated Quest Pro: gaze on the target with 0.7° noise; every 9th presentation the eye is 6° away.
            var rng = new Perimetry.Core.MathUtil.Rng(99);
            PresentationTimeline seen = null; int n = 0; bool away = false;
            d.Gaze = (drv, target) =>
            {
                var tl = drv.Flow.CurrentPresentation;
                if (tl != null && tl != seen) { seen = tl; n++; away = n % 9 == 0; }
                double ex = away ? 6.0 : rng.Gaussian(0, 0.7), ey = away ? 0 : rng.Gaussian(0, 0.7);
                return new FieldPoint(target.X + ex, target.Y + ey);
            };
            d.RunUntil(() => flow.State == FlowState.Exam);
            Assert.That(flow.GazeAvailable, Is.True);
            Assert.That(flow.View.GazeAvailable, Is.True);
            d.RunUntil(() => flow.State == FlowState.BetweenEyes || flow.IsFinished);

            var exam = sink.Exams[0];
            var rel = exam.Reliability;
            Assert.That(rel.GazeTracked, Is.True);
            Assert.That(rel.GazeRejectedPresentations, Is.GreaterThan(3));
            Assert.That(exam.Presentations.Count(p => p.Disposition == "gazeRejected"), Is.EqualTo(rel.GazeRejectedPresentations));
            Assert.That(exam.Presentations.Where(p => p.Disposition == "gazeRejected").All(p => p.Result.GazeErrorDeg > Env.Config.Reliability.Gaze.MaxErrorDeg));
            Assert.That(exam.Presentations.Where(p => p.Request.Kind == PresentationKind.Threshold).All(p => p.Result.GazeTracked));
            Assert.That(rel.GazeTrace.Count, Is.GreaterThan(100));
            Assert.That(rel.GazeTrace.Count(s => s.Rejected), Is.EqualTo(rel.GazeRejectedPresentations));
            Assert.That(rel.GazeMeanErrorDeg, Is.GreaterThan(0).And.LessThan(3));
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotCatch), Is.False, "gaze is the fixation monitor: no Heijl-Krakau");
            Assert.That(exam.Locations.Where(l => !l.IsBlindSpot).All(l => !double.IsNaN(l.ThresholdDb)), "rejected points were retested");
            Assert.That(sink.Logs.Count == 0 || sink.Logs[0].Events.Any(e => e.Type == "gaze" && e.Detail == "available"));
        }

        [Test]
        public void Flow_WithoutEyeTracker_NothingIsTracked()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-od", AgeYears = 60, PatientRef = "P-Q3", Seed = 11 });
            var d = new FlowDriver(flow, 21);
            d.RunUntil(() => flow.IsFinished);
            Assert.That(flow.GazeAvailable, Is.False);
            var exam = sink.Exams.Single();
            Assert.That(exam.Reliability.GazeTracked, Is.False);
            Assert.That(exam.Reliability.GazeRejectedPresentations, Is.EqualTo(0));
            Assert.That(exam.Reliability.GazeTrace, Is.Empty);
            Assert.That(exam.Presentations.All(p => !p.Result.GazeTracked && p.Result.GazeErrorDeg == null));
            Assert.That(exam.Foveal, Is.Null);
            Assert.That(exam.Settings.ResponseMode, Is.EqualTo(ResponseMode.YesNo));
            Assert.That(exam.Presentations.All(p => p.Request.TargetX == 0 && p.Request.TargetY == 0 && p.Request.Intervals == 0));
        }

        [Test]
        public void Flow_ForcedChoicePreset_PatientAnswersByInterval_ExamCompletes()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-fast-forced-choice", AgeYears = 50, PatientRef = "P-FC", Seed = 31 });
            var d = new FlowDriver(flow, 41);
            d.RunUntil(() => flow.State == FlowState.Instructions);
            Assert.That(flow.View.Message, Does.Contain("piscar"));
            bool markerSeen = false, fixationHiddenOnMarker = true;
            d.RunUntil(() =>
            {
                var tl = flow.CurrentPresentation;
                if (flow.State == FlowState.Exam && tl != null && tl.FixationMarker)
                {
                    markerSeen = true;
                    if (flow.View.ShowFixation) fixationHiddenOnMarker = false;
                }
                return flow.State == FlowState.BetweenEyes || flow.IsFinished;
            }, 90 * 60 * 1000);
            Assert.That(markerSeen && fixationHiddenOnMarker, "the target blinks off on the markers");

            var exam = sink.Exams[0];
            Assert.That(exam.Settings.ResponseMode, Is.EqualTo(ResponseMode.ForcedChoice));
            Assert.That(exam.Aborted, Is.False);
            Assert.That(exam.Presentations.All(p => p.Request.Intervals == 2 && p.Request.StimulusInterval >= 1));
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.FalsePositiveCatch), Is.False, "no FP catch trials in forced choice");
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotCatch), Is.False, "no Heijl-Krakau in forced choice");
            Assert.That(exam.Presentations.Count(p => p.Result.RespondedInterval.HasValue), Is.GreaterThan(exam.Presentations.Count / 2), "the simulated patient always picks an interval when seeing");
            Assert.That(exam.Presentations.Where(p => p.Disposition == "used").All(p => p.Result.RespondedInterval == null || p.Result.RespondedInterval >= 1 && p.Result.RespondedInterval <= 2));
            Assert.That(exam.Locations.Where(l => !l.IsBlindSpot).All(l => !double.IsNaN(l.ThresholdDb)));
            Assert.That(exam.Reliability.AnticipatedResponses, Is.EqualTo(0), "no anticipation rule in forced choice");
        }

        // ------------------------------------------------------------------ live snapshot

        [Test]
        public void LiveSnapshot_CarriesPhase5Fields()
        {
            var sink = new MemorySink();
            var flow = new PatientFlow(Env.Config, Env.Headset, Env.Lum, sink,
                new ExamOrder { PresetId = "24-2-zest-fast-foveal", AgeYears = 55, PatientRef = "P-FOV", Seed = 5 });
            var d = new FlowDriver(flow, 7);
            d.RunUntil(() => flow.State == FlowState.Exam && flow.CurrentSession != null && flow.CurrentSession.BuildRecord().Reliability.TotalPresentations > 30);
            var snap = LiveSnapshot.Build(flow, Env.Config, 1, DateTime.UtcNow, null);
            Assert.That((string)snap["schema"], Is.EqualTo(PortalSync.Schema));
            Assert.That((bool)snap["gazeAvailable"], Is.False);
            Assert.That((string)snap["fixation"]["kind"], Is.EqualTo("Central"));
            var exam = (JObject)snap["exam"];
            Assert.That((string)exam["fixationMode"], Is.EqualTo("CentralHeijlKrakau"));
            Assert.That((string)exam["responseMode"], Is.EqualTo("YesNo"));
            Assert.That((bool)exam["fovealThreshold"], Is.True);
            Assert.That(exam["foveal"].Type, Is.EqualTo(JTokenType.Object), "foveal test finished before the grid");
            Assert.That((double)exam["foveal"]["hfaEqDb"], Is.EqualTo((double)exam["foveal"]["db"] + (double)exam["hfaOffsetDb"]).Within(0.011));
            Assert.That((bool)exam["reliability"]["gazeTracked"], Is.False);
            Assert.That((int)exam["reliability"]["gazeRejectedPresentations"], Is.EqualTo(0));
            Assert.That(exam["reliability"]["gazeMeanErrorDeg"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(exam["last"]["targetX"], Is.Not.Null);
        }
    }
}
