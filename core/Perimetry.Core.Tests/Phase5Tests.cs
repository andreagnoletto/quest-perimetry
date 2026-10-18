using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    /// <summary>Phase 5: foveal threshold, eye tracking, oculokinetic fixation, forced choice, GHT applicability.</summary>
    public class Phase5Tests
    {
        private static (ExamRecord exam, SyntheticField field, SimulatedObserver observer) Run(ExamSettings s, ObserverParameters obs = null,
            DefectType defect = DefectType.Normal, ClinicalConfig cfg = null)
        {
            cfg ??= TestEnv.Config;
            var grid = GridLibrary.Get(cfg, s.GridId);
            var norm = ProvisionalNormative.For(cfg, grid);
            var rng = new Rng(s.Seed * 7919 + 17);
            var field = SyntheticField.Create(cfg.ProvisionalNormative, grid, s.AgeYears, defect, 15, rng);
            var observer = new SimulatedObserver(field, TestEnv.Scale, s.Eye, obs ?? new ObserverParameters(), rng.Fork());
            var session = new ExamSession(s, cfg, norm, TestEnv.Scale);
            return (ExamSimulator.Run(session, observer), field, observer);
        }

        private static double Mae(ExamRecord exam, SyntheticField field)
        {
            var pts = exam.Locations.Where(l => !l.IsBlindSpot && TestEnv.Scale.FromHfaEquivalent(field.TrueHfaDb[l.Index]) >= 0).ToList();
            return pts.Average(l => Math.Abs(l.ThresholdDb + exam.HfaOffsetDb - field.TrueHfaDb[l.Index]));
        }

        private static ClinicalConfig ConfigWith(string file, string from, string to)
        {
            var files = ClinicalConfig.ReadFiles(TestEnv.ConfigDir);
            Assert.That(files[file], Does.Contain(from));
            files[file] = files[file].Replace(from, to);
            return ClinicalConfig.LoadFromFiles(files);
        }

        [Test]
        public void Config_LoadsPhase5Sections()
        {
            var c = TestEnv.Config;
            Assert.That(c.Strategies.Foveal.FixationTarget, Is.EqualTo("Diamond4"));
            Assert.That(c.Strategies.Foveal.DiamondHalfSizeDeg, Is.EqualTo(1.5));
            Assert.That(c.Strategies.Foveal.Strategy, Is.EqualTo("exam"));
            Assert.That(c.Strategies.ForcedChoice.Intervals, Is.EqualTo(2));
            Assert.That(c.Strategies.ForcedChoice.IntervalMs, Is.EqualTo(200));
            Assert.That(c.Strategies.ForcedChoice.GapMs, Is.EqualTo(600));
            Assert.That(c.Strategies.ForcedChoice.MarkerMs, Is.EqualTo(100));
            Assert.That(c.Reliability.Gaze.MaxErrorDeg, Is.EqualTo(3.0));
            Assert.That(c.Reliability.Gaze.RetestRejected, Is.True);
            Assert.That(c.Reliability.Oculokinetic.TargetRangeDeg, Is.EqualTo(10));
            Assert.That(c.Reliability.Oculokinetic.MaxEccentricityDeg, Is.EqualTo(45));
            Assert.That(c.Reliability.Oculokinetic.LockDelayMs, Is.EqualTo(600));
            Assert.That(c.Reliability.Oculokinetic.GazeConfirmMs, Is.EqualTo(300));
            Assert.That(c.Reliability.Oculokinetic.HeijlKrakau, Is.True);
            Assert.That(c.ProvisionalNormative.FovealExtraDb, Is.EqualTo(1.5));
            Assert.That(TestEnv.Grid("10-2").GhtApplicable, Is.False);
            foreach (var g in new[] { "24-2", "30-2", "24-2C" }) Assert.That(TestEnv.Grid(g).GhtApplicable, Is.True, g);
            // a grid without the key stays applicable (older grid files)
            Assert.That(TestGrid.FromCsv("c", "x,y\n9,9\n-9,9\n9,-9\n-9,-9\n").GhtApplicable, Is.True);
        }

        [Test]
        public void AngularGeometry_ComposeIsIdentityAtCentre_InvertsAndPreservesEccentricity()
        {
            var p = new FieldPoint(-9, 15);
            Assert.That(AngularGeometry.Compose(new FieldPoint(0, 0), p), Is.EqualTo(p));
            Assert.That(AngularGeometry.Relative(new FieldPoint(0, 0), p), Is.EqualTo(p));
            var targets = new[] { new FieldPoint(10, 0), new FieldPoint(0, -10), new FieldPoint(-7, 8), new FieldPoint(3, 3) };
            var points = new[] { new FieldPoint(3, 3), new FieldPoint(-27, 3), new FieldPoint(15, -1.5), new FieldPoint(0, 0), new FieldPoint(9, -21) };
            foreach (var t in targets)
                foreach (var q in points)
                {
                    var abs = AngularGeometry.Compose(t, q);
                    var back = AngularGeometry.Relative(t, abs);
                    Assert.That(back.X, Is.EqualTo(q.X).Within(1e-6), $"{t} {q}");
                    Assert.That(back.Y, Is.EqualTo(q.Y).Within(1e-6), $"{t} {q}");
                    // the stimulus keeps its angular distance from the target (same retinal eccentricity)
                    // acos near 1 loses precision for the centre point: 1e-4 degrees is far below any rendering resolution
                    Assert.That(AngularGeometry.AngleBetweenDeg(t, abs), Is.EqualTo(AngularGeometry.Eccentricity(q)).Within(1e-4), $"{t} {q}");
                }
            // the centre of the grid lands on the target
            Assert.That(AngularGeometry.Compose(new FieldPoint(-7, 8), new FieldPoint(0, 0)), Is.EqualTo(new FieldPoint(-7, 8)));
            // small angles: nearly additive
            var a = AngularGeometry.Compose(new FieldPoint(10, 0), new FieldPoint(3, 3));
            Assert.That(a.X, Is.EqualTo(13).Within(0.3));
            Assert.That(a.Y, Is.EqualTo(3).Within(0.3));
            Assert.That(AngularGeometry.Eccentricity(new FieldPoint(0, 0)), Is.EqualTo(0).Within(1e-9));
            Assert.That(AngularGeometry.Eccentricity(new FieldPoint(30, 0)), Is.EqualTo(30).Within(1e-9));
        }

        [Test]
        public void Foveal_IsTestedFirst_WithDiamondTarget_AndAnalysed()
        {
            var s = new ExamSettings { Strategy = StrategyKind.Zest, Seed = 51, FovealThreshold = true };
            var (exam, field, obs) = Run(s);
            var fov = exam.Presentations.Where(p => p.Request.Kind == PresentationKind.Foveal).ToList();
            Assert.That(fov, Is.Not.Empty);
            Assert.That(exam.Presentations.TakeWhile(p => p.Request.Kind == PresentationKind.Foveal).Count(), Is.EqualTo(fov.Count), "foveal presentations come first");
            Assert.That(exam.Presentations[fov.Count].Request.Kind, Is.EqualTo(PresentationKind.BlindSpotMapping));
            foreach (var p in fov)
            {
                Assert.That(p.Request.FixationTarget, Is.EqualTo(FixationTargetKind.Diamond4));
                Assert.That(p.Request.FieldX, Is.EqualTo(0)); Assert.That(p.Request.FieldY, Is.EqualTo(0));
                Assert.That(p.Request.LocationIndex, Is.EqualTo(-1));
                Assert.That(p.Disposition, Is.EqualTo("foveal"));
            }
            Assert.That(exam.Presentations.Skip(fov.Count).All(p => p.Request.FixationTarget == FixationTargetKind.Central));
            Assert.That(exam.Foveal, Is.Not.Null);
            Assert.That(exam.Foveal.Strategy, Is.EqualTo("Zest"));
            Assert.That(exam.Foveal.Presentations, Is.EqualTo(fov.Count));
            double truth = obs.TrueThresholdAt(new FieldPoint(0, 0));
            Assert.That(exam.Foveal.ThresholdDb + exam.HfaOffsetDb, Is.EqualTo(truth).Within(4));
            Assert.That(exam.Locations.All(l => !l.IsBlindSpot ? l.Presentations > 0 : true), "main test still complete");

            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.FovealTested);
            Assert.That(a.FovealHfaDb, Is.EqualTo(exam.Foveal.ThresholdDb + exam.HfaOffsetDb).Within(1e-9));
            Assert.That(a.FovealExpectedHfaDb, Is.GreaterThan(30).And.LessThan(40));
            Assert.That(a.FovealTd, Is.EqualTo(a.FovealHfaDb - a.FovealExpectedHfaDb).Within(1e-9));
            Assert.That(a.FovealTdP, Is.InRange(0, 1));

            // off by default: nothing foveal anywhere
            var (exam2, _, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 52 });
            Assert.That(exam2.Foveal, Is.Null);
            Assert.That(exam2.Presentations.Any(p => p.Request.Kind == PresentationKind.Foveal), Is.False);
            Assert.That(FieldAnalysis.Analyze(exam2, TestEnv.Config, TestEnv.Norm()).FovealTested, Is.False);
        }

        [Test]
        public void Foveal_StrategyResolution_AndScreeningUsesZest()
        {
            Assert.That(ExamSession.FovealStrategyKind(StrategyKind.FullThreshold, "exam"), Is.EqualTo(StrategyKind.FullThreshold));
            Assert.That(ExamSession.FovealStrategyKind(StrategyKind.Screening, "exam"), Is.EqualTo(StrategyKind.Zest));
            Assert.That(ExamSession.FovealStrategyKind(StrategyKind.Zest, "FourTwo"), Is.EqualTo(StrategyKind.FourTwo));
            Assert.Throws<ArgumentException>(() => ExamSession.FovealStrategyKind(StrategyKind.Zest, "Screening"));

            var (exam, _, _) = Run(new ExamSettings { Strategy = StrategyKind.Screening, Seed = 53, FovealThreshold = true });
            Assert.That(exam.Foveal.Strategy, Is.EqualTo("Zest"));
            Assert.That(double.IsNaN(exam.Foveal.ThresholdDb), Is.False);
            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.FovealTested);
            Assert.That(double.IsNaN(a.Md), "screening still has no indices");
        }

        [Test]
        public void Normative_HasFovealAgeRegression()
        {
            var n = TestEnv.Norm();
            var nearest = TestEnv.Grid().Locations.OrderBy(l => l.Point.Eccentricity).First();
            Assert.That(n.ExpectedFovealHfa(50), Is.GreaterThan(n.ExpectedHfa(nearest.Index, 50)));
            Assert.That(n.ExpectedFovealHfa(70), Is.LessThan(n.ExpectedFovealHfa(30)), "declines with age");
            Assert.That(n.FovealTdProbability(0), Is.EqualTo(1));
            Assert.That(n.FovealTdProbability(-12), Is.EqualTo(0.005));
            var db = (NormativeDatabase)n;
            Assert.That(db.FovealPopulationSize, Is.EqualTo(db.PopulationSize));
            Assert.That(db.FovealTdSd, Is.GreaterThan(0.5).And.LessThan(4));
            // a population without foveal values builds a database with no foveal data
            var pop = ProvisionalNormative.GeneratePopulation(TestEnv.Config.ProvisionalNormative, TestEnv.Grid()).Take(200)
                .Select(s => new NormalSubject { Id = s.Id, Age = s.Age, Sensitivity = s.Sensitivity }).ToList();
            var none = NormativeBuilder.Build("nofovea", false, TestEnv.Grid(), pop, TestEnv.Config.Analysis, TestEnv.Config.Ght);
            Assert.That(double.IsNaN(none.ExpectedFovealHfa(50)));
            Assert.That(none.FovealTdProbability(-12), Is.EqualTo(1));
            // and a session still starts the foveal procedure from the nearest point's expected value
            var session = new ExamSession(new ExamSettings { FovealThreshold = true, Seed = 1 }, TestEnv.Config, none, TestEnv.Scale);
            Assert.That(session.Next().Kind, Is.EqualTo(PresentationKind.Foveal));
        }

        [Test]
        public void EyeTracking_RejectsGazeBeyondLimit_RetestsAndRecordsTrace()
        {
            var obs = new ObserverParameters { GazeNoiseSdDeg = 1.0, FixationLossRate = 0.2 };
            var (exam, field, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 54, FixationMode = FixationMode.CentralEyeTracking }, obs);
            var thr = exam.Presentations.Where(p => p.Request.Kind == PresentationKind.Threshold).ToList();
            var rejected = thr.Where(p => p.Disposition == "gazeRejected").ToList();
            Assert.That(rejected.Count, Is.GreaterThan(10));
            Assert.That(rejected.All(p => p.Result.GazeErrorDeg > 3.0));
            Assert.That(thr.Where(p => p.Disposition == "used").All(p => p.Result.GazeErrorDeg <= 3.0));
            Assert.That(exam.Locations.Where(l => !l.IsBlindSpot).All(l => l.StopReason != "NotStarted" && l.Presentations > 0), "rejected points were retested");
            var rel = exam.Reliability;
            Assert.That(rel.GazeTracked);
            Assert.That(rel.GazeRejectedPresentations, Is.EqualTo(rejected.Count));
            Assert.That(rel.GazeSamples, Is.EqualTo(thr.Count));
            Assert.That(rel.GazeTrace.Count, Is.EqualTo(thr.Count));
            Assert.That(rel.GazeTrace.Count(g => g.Rejected), Is.EqualTo(rejected.Count));
            Assert.That(rel.GazeMeanErrorDeg, Is.EqualTo(rel.GazeTrace.Average(g => g.ErrorDeg)).Within(1e-9));
            Assert.That(rel.GazeTrace.Select(g => g.OnsetMs), Is.Ordered);
            // the rejections protect the thresholds: presentations during a loss are not used
            Assert.That(Mae(exam, field), Is.LessThan(3.0));

            // Heijl-Krakau mode: the trace is recorded but the rule does not apply (HK monitors fixation)
            var (hk, _, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 55 }, new ObserverParameters { GazeNoiseSdDeg = 1.0, FixationLossRate = 0.2 });
            Assert.That(hk.Reliability.GazeTracked);
            Assert.That(hk.Presentations.Any(p => p.Disposition == "gazeRejected"), Is.False);
            Assert.That(hk.Reliability.GazeRejectedPresentations, Is.EqualTo(0));

            // no tracker: nothing recorded
            var (plain, _, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 56, FixationMode = FixationMode.CentralEyeTracking });
            Assert.That(plain.Reliability.GazeTracked, Is.False);
            Assert.That(plain.Reliability.GazeTrace, Is.Empty);
            Assert.That(double.IsNaN(plain.Reliability.GazeMeanErrorDeg));
        }

        [Test]
        public void Oculokinetic_TargetJumps_StimuliRelativeToTarget_WithinField()
        {
            var o = TestEnv.Config.Reliability.Oculokinetic;
            foreach (var eye in new[] { Eye.OD, Eye.OS })
            {
                var (exam, field, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 57, Eye = eye, FixationMode = FixationMode.Oculokinetic });
                var grid = TestEnv.Grid();
                var thr = exam.Presentations.Where(p => p.Request.Kind == PresentationKind.Threshold).ToList();
                Assert.That(thr.Select(p => (p.Request.TargetX, p.Request.TargetY)).Distinct().Count(), Is.GreaterThan(thr.Count / 2), "target jumps");
                foreach (var p in thr)
                {
                    var r = p.Request;
                    Assert.That(Math.Abs(r.TargetX), Is.LessThanOrEqualTo(o.TargetRangeDeg));
                    Assert.That(Math.Abs(r.TargetY), Is.LessThanOrEqualTo(o.TargetRangeDeg));
                    var gp = grid.Locations[r.LocationIndex].Point.ForEye(eye);
                    Assert.That(r.FieldX, Is.EqualTo(gp.X)); Assert.That(r.FieldY, Is.EqualTo(gp.Y));
                    var abs = AngularGeometry.Compose(new FieldPoint(r.TargetX, r.TargetY), new FieldPoint(r.FieldX, r.FieldY));
                    Assert.That(AngularGeometry.Eccentricity(abs), Is.LessThanOrEqualTo(o.MaxEccentricityDeg));
                    Assert.That(r.FixationTarget, Is.EqualTo(FixationTargetKind.Central));
                }
                // Heijl-Krakau stays on (relative to the moving target)
                Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotMapping));
                Assert.That(exam.Reliability.BlindSpotFound);
                Assert.That(exam.Presentations.Where(p => p.Request.Kind == PresentationKind.BlindSpotCatch).Count(), Is.GreaterThan(0));
                Assert.That(Mae(exam, field), Is.LessThan(3.0), eye.ToString());
            }
            // with heijlKrakau off there is no blind-spot search in mode B
            var cfg = ConfigWith("reliability.json", "\"heijlKrakau\": true", "\"heijlKrakau\": false");
            var (noHk, _, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 58, FixationMode = FixationMode.Oculokinetic }, cfg: cfg);
            Assert.That(noHk.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotMapping), Is.False);
            Assert.That(FieldAnalysis.Analyze(noHk, cfg, ProvisionalNormative.For(cfg, GridLibrary.Get(cfg, "24-2"))).Warnings, Has.None.Contains("Mancha cega"));
        }

        [Test]
        public void ForcedChoice_IntervalsGuessRate_NoFalsePositiveTrials_NoHeijlKrakau()
        {
            var f = new ProcedureFactory(StrategyKind.Zest, TestEnv.Config.Strategies, TestEnv.Scale, 0.5);
            Assert.That(f.GuessRate, Is.EqualTo(0.5));
            Assert.That(new ProcedureFactory(StrategyKind.Zest, TestEnv.Config.Strategies, TestEnv.Scale).GuessRate, Is.EqualTo(0.03));
            var like = ZestProcedure.GaussianLikelihood(new double[] { 0, 30 }, 0.5, 0.03, _ => 1.0);
            Assert.That(like[1, 0], Is.EqualTo(0.5).Within(1e-6), "a stimulus far below threshold is 'seen' at chance");
            Assert.That(like[0, 1], Is.EqualTo(0.97).Within(1e-6));

            var s = new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 59, ResponseMode = ResponseMode.ForcedChoice };
            var (exam, field, _) = Run(s);
            var fc = TestEnv.Config.Strategies.ForcedChoice;
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.FalsePositiveCatch), Is.False);
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotMapping || p.Request.Kind == PresentationKind.BlindSpotCatch), Is.False);
            Assert.That(exam.Reliability.FalsePositives.Trials, Is.EqualTo(0));
            Assert.That(exam.Reliability.FalseNegatives.Trials, Is.GreaterThan(0), "FN catch trials still run");
            foreach (var p in exam.Presentations)
            {
                Assert.That(p.Request.Intervals, Is.EqualTo(fc.Intervals));
                Assert.That(p.Request.StimulusInterval, Is.InRange(1, fc.Intervals));
                Assert.That(p.Request.IntervalMs, Is.EqualTo(fc.IntervalMs));
                Assert.That(p.Request.IntervalGapMs, Is.EqualTo(fc.GapMs));
                Assert.That(p.Request.MarkerMs, Is.EqualTo(fc.MarkerMs));
                Assert.That(p.Request.DurationMs, Is.EqualTo(fc.IntervalMs));
                Assert.That(p.Result.RespondedInterval, Is.Not.Null, "the simulated patient always chooses");
            }
            Assert.That(exam.Presentations.Select(p => p.Request.StimulusInterval).Distinct().Count(), Is.EqualTo(fc.Intervals));
            Assert.That(ExamSession.StimulusSpanMs(exam.Presentations[0].Request), Is.EqualTo(2 * 200 + 600));
            Assert.That(Mae(exam, field), Is.LessThan(4.0));
            var (yesNo, _, _) = Run(new ExamSettings { Strategy = StrategyKind.ZestFast, Seed = 59 });
            Assert.That(exam.Reliability.TotalPresentations, Is.GreaterThan(yesNo.Reliability.TotalPresentations), "chance responses cost presentations");

            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.Warnings, Has.Some.Contains("Escolha forçada"));
            Assert.That(a.Warnings, Has.None.Contains("Mancha cega"));

            // more intervals -> lower chance level -> fewer presentations
            var cfg4 = ConfigWith("strategies.json", "\"intervals\": 2", "\"intervals\": 4");
            var (four, _, _) = Run(s, cfg: cfg4);
            Assert.That(four.Presentations.All(p => p.Request.Intervals == 4));
            Assert.That(four.Reliability.TotalPresentations, Is.LessThan(exam.Reliability.TotalPresentations));

            var cfg1 = ConfigWith("strategies.json", "\"intervals\": 2", "\"intervals\": 1");
            Assert.Throws<ArgumentException>(() => new ExamSession(s, cfg1, ProvisionalNormative.For(cfg1, GridLibrary.Get(cfg1, "24-2")), TestEnv.Scale));
        }

        [Test]
        public void ForcedChoice_WrongIntervalIsNotSeen_AndNoAnticipationRule()
        {
            var s = new ExamSettings { Strategy = StrategyKind.FullThreshold, Seed = 60, ResponseMode = ResponseMode.ForcedChoice, FalseNegativeCatchTrials = false };
            var session = new ExamSession(s, TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);
            var r1 = session.Next();
            Assert.That(r1.Kind, Is.EqualTo(PresentationKind.Threshold));
            int wrong = r1.StimulusInterval == 1 ? 2 : 1;
            session.Submit(new PresentationResult { Sequence = r1.Sequence, Responded = true, RespondedInterval = wrong, LatencyMs = 20, OnsetMs = 0 });
            // wrong interval = not seen: the 4-2 staircase goes 4 dB brighter at that location
            Assert.That(session.Strategy.ProcedureOf(r1.LocationIndex).NextStimulusDb(), Is.EqualTo(r1.Db - 4));
            var r2 = session.Next();
            session.Submit(new PresentationResult { Sequence = r2.Sequence, Responded = true, RespondedInterval = r2.StimulusInterval, LatencyMs = 20, OnsetMs = 2000 });
            Assert.That(session.Strategy.ProcedureOf(r2.LocationIndex).NextStimulusDb(), Is.EqualTo(r2.Db + 4), "correct interval = seen: dimmer");
            var r3 = session.Next();
            session.Submit(new PresentationResult { Sequence = r3.Sequence, Responded = false, OnsetMs = 4000 });
            Assert.That(session.Strategy.ProcedureOf(r3.LocationIndex).NextStimulusDb(), Is.EqualTo(r3.Db - 4), "no answer = not seen");
            var rec = session.BuildRecord();
            Assert.That(rec.Presentations.Select(p => p.Disposition), Is.All.EqualTo("used"), "a 20 ms latency is not 'anticipated' in forced choice");
            Assert.That(rec.Reliability.AnticipatedResponses, Is.EqualTo(0));
            Assert.That(rec.Reliability.DurationMs, Is.GreaterThanOrEqualTo(4000 + 1000 + 1000), "span of both intervals + window counted");
        }

        [Test]
        public void Ght_NotApplicableOn10_2()
        {
            var (exam, _) = TestEnv.Simulate(61, grid: "10-2");
            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm("10-2"));
            Assert.That(a.GhtApplicable, Is.False);
            Assert.That(a.Ght, Is.EqualTo(GhtResult.NotApplicable));
            Assert.That(a.GhtSectorDifferences, Is.Empty);
            Assert.That(a.Warnings, Has.Some.Contains("GHT não se aplica"));
            Assert.That(double.IsNaN(a.Md), Is.False, "MD/PSD/VFI are still computed");
            Assert.That(a.Points.Count, Is.EqualTo(68));
            var (e24, _) = TestEnv.Simulate(61);
            var a24 = FieldAnalysis.Analyze(e24, TestEnv.Config, TestEnv.Norm());
            Assert.That(a24.GhtApplicable);
            Assert.That(a24.Ght, Is.Not.EqualTo(GhtResult.NotApplicable));
            Assert.That(a24.GhtSectorDifferences.Length, Is.EqualTo(5));
        }

        [Test]
        public void Settings_RoundTripJson_WithPhase5Enums()
        {
            var s = new ExamSettings { FixationMode = FixationMode.Oculokinetic, ResponseMode = ResponseMode.ForcedChoice, FovealThreshold = true, GridId = "24-2C" };
            string json = JsonConvert.SerializeObject(s);
            Assert.That(json, Does.Contain("\"Oculokinetic\"").And.Contain("\"ForcedChoice\"").And.Contain("\"FovealThreshold\":true"));
            var back = JsonConvert.DeserializeObject<ExamSettings>(json);
            Assert.That(back.FixationMode, Is.EqualTo(FixationMode.Oculokinetic));
            Assert.That(back.ResponseMode, Is.EqualTo(ResponseMode.ForcedChoice));
            Assert.That(back.FovealThreshold);
            // phase 1-4 files without the new keys keep the HFA defaults
            var old = JsonConvert.DeserializeObject<ExamSettings>("{\"GridId\":\"24-2\",\"Eye\":\"OS\",\"Strategy\":\"Zest\"}");
            Assert.That(old.ResponseMode, Is.EqualTo(ResponseMode.YesNo));
            Assert.That(old.FovealThreshold, Is.False);
            Assert.That(old.FixationMode, Is.EqualTo(FixationMode.CentralHeijlKrakau));
        }
    }
}
