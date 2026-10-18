using System;
using System.Linq;
using Newtonsoft.Json;
using NUnit.Framework;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    public class StrategyAndSessionTests
    {
        [Test]
        public void GrowthPattern_StartsWithAnchors_ThenNeighbours()
        {
            var g = TestEnv.Grid();
            var f = new ProcedureFactory(StrategyKind.FullThreshold, TestEnv.Config.Strategies, TestEnv.Scale);
            var expected = Enumerable.Repeat(15.0, g.Count).ToList();
            var s = new ThresholdStrategy(g, f, expected, true, new Rng(1));
            Assert.That(g.Locations.Where(l => s.StateOf(l.Index) == ThresholdStrategy.LocationState.Active).All(l => l.IsAnchor));
            // run with a field 4 dB below expected everywhere: neighbours should start shifted by about -4
            int last = -1;
            while (!s.IsFinished)
            {
                int i = s.PickNextLocation().Value;
                if (g.Locations.Count(l => s.StateOf(l.Index) == ThresholdStrategy.LocationState.Active) > 1)
                    Assert.That(i, Is.Not.EqualTo(last), "no immediate repeat");
                last = i;
                double st = s.NextStimulusDb(i);
                s.Update(i, st, st <= 11);
            }
            var nonAnchor = g.Locations.Where(l => !l.IsAnchor).ToList();
            Assert.That(nonAnchor.Average(l => s.StartValueOf(l.Index)), Is.LessThan(13));
            Assert.That(s.PickNextLocation(), Is.Null);
            Assert.Throws<InvalidOperationException>(() => s.Update(0, 10, true));
            Assert.Throws<ArgumentException>(() => new ThresholdStrategy(g, f, new double[3], true, new Rng(1)));
        }

        [Test]
        public void WithoutSeeding_AllLocationsStartActive()
        {
            var g = TestEnv.Grid();
            var f = new ProcedureFactory(StrategyKind.Screening, TestEnv.Config.Strategies, TestEnv.Scale);
            var s = new ThresholdStrategy(g, f, Enumerable.Repeat(15.0, g.Count).ToList(), true, new Rng(1));
            Assert.That(g.Locations.All(l => s.StateOf(l.Index) == ThresholdStrategy.LocationState.Active));
            Assert.That(s.ExpectedOf(0), Is.EqualTo(15));
            Assert.That(s.ProcedureOf(0), Is.TypeOf<ScreeningProcedure>());
        }

        [Test]
        public void IsolatedPoints_AreStartedFromGlobalShift()
        {
            var g = TestGrid.FromCsv("islands", "x,y\n9,9\n-9,9\n9,-9\n-9,-9\n27,27\n", neighbourRadiusDeg: 1);
            var f = new ProcedureFactory(StrategyKind.FourTwo, TestEnv.Config.Strategies, TestEnv.Scale);
            var s = new ThresholdStrategy(g, f, Enumerable.Repeat(15.0, g.Count).ToList(), true, new Rng(2));
            while (!s.IsFinished) { int i = s.PickNextLocation().Value; double st = s.NextStimulusDb(i); s.Update(i, st, st <= 15); }
            Assert.That(s.StateOf(4), Is.EqualTo(ThresholdStrategy.LocationState.Finished));
        }

        [Test]
        public void Session_IsDeterministicForASeed()
        {
            var a = TestEnv.Simulate(11).exam;
            var b = TestEnv.Simulate(11).exam;
            Assert.That(a.Presentations.Count, Is.EqualTo(b.Presentations.Count));
            Assert.That(a.Presentations.Select(p => (p.Request.LocationIndex, p.Request.Db, p.Result.Responded)),
                Is.EqualTo(b.Presentations.Select(p => (p.Request.LocationIndex, p.Request.Db, p.Result.Responded))));
            Assert.That(a.Locations.Select(l => l.ThresholdDb), Is.EqualTo(b.Locations.Select(l => l.ThresholdDb)));
        }

        [Test]
        public void Session_LogsEveryPresentationWithAuditFields()
        {
            var (exam, _) = TestEnv.Simulate(3);
            Assert.That(exam.Presentations.Count, Is.EqualTo(exam.Reliability.TotalPresentations));
            Assert.That(exam.ConfigHash, Is.EqualTo(TestEnv.Config.Hash));
            Assert.That(exam.Calibrated, Is.False);
            Assert.That(exam.NormativeProvisional, Is.True);
            Assert.That(exam.Disclaimer, Does.Contain("ANVISA"));
            foreach (var p in exam.Presentations)
            {
                Assert.That(p.Result.Sequence, Is.EqualTo(p.Request.Sequence));
                Assert.That(p.Request.PreStimulusDelayMs, Is.InRange(TestEnv.Config.Timing.IsiMinMs, TestEnv.Config.Timing.IsiMaxMs));
                Assert.That(p.Request.DurationMs, Is.EqualTo(200));
                Assert.That(p.Request.SizeDeg, Is.EqualTo(0.431).Within(0.001));
                if (p.Request.StimulusShown) Assert.That(p.Request.LuminanceCdm2, Is.EqualTo(TestEnv.Scale.DbToStimulusLuminance(p.Request.Db)).Within(1e-9));
                if (p.Result.Responded) Assert.That(p.Result.LatencyMs, Is.Not.Null);
            }
            var kinds = exam.Presentations.Select(p => p.Request.Kind).Distinct().ToList();
            Assert.That(kinds, Is.SupersetOf(new[] { PresentationKind.Threshold, PresentationKind.FalsePositiveCatch,
                PresentationKind.FalseNegativeCatch, PresentationKind.BlindSpotCatch, PresentationKind.BlindSpotMapping }));
            Assert.That(exam.Locations.All(l => l.StopReason != "NotStarted"));
            // serialisable for storage / sync
            var json = JsonConvert.SerializeObject(exam, new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.Symbol });
            var back = JsonConvert.DeserializeObject<ExamRecord>(json);
            Assert.That(back.Presentations.Count, Is.EqualTo(exam.Presentations.Count));
            Assert.That(back.Settings.Strategy, Is.EqualTo(StrategyKind.ZestFast));
        }

        [Test]
        public void BlindSpot_IsFoundAtNominalPosition_ForNormalObserver()
        {
            var (exam, _) = TestEnv.Simulate(5);
            Assert.That(exam.Reliability.BlindSpotFound);
            Assert.That(exam.Reliability.BlindSpotX, Is.EqualTo(15));
            Assert.That(exam.Reliability.BlindSpotY, Is.EqualTo(-1.5));
            Assert.That(exam.Reliability.FixationLosses.Trials, Is.GreaterThan(0));
            Assert.That(exam.Reliability.FixationLosses.Errors, Is.LessThanOrEqualTo(1));
        }

        [Test]
        public void LeftEye_PresentsMirroredCoordinates()
        {
            var (exam, _) = TestEnv.Simulate(5, eye: Eye.OS);
            Assert.That(exam.Reliability.BlindSpotX, Is.EqualTo(-15));
            var g = TestEnv.Grid();
            foreach (var p in exam.Presentations.Where(p => p.Request.Kind == PresentationKind.Threshold))
                Assert.That(p.Request.FieldX, Is.EqualTo(-g.Locations[p.Request.LocationIndex].Point.X));
        }

        [Test]
        public void FixationLosses_AreDetectedByHeijlKrakau()
        {
            var (exam, _) = TestEnv.Simulate(6, obs: new ObserverParameters { FixationLossRate = 0.5 });
            Assert.That(exam.Reliability.FixationLosses.Rate, Is.GreaterThan(0.2));
            Assert.That(exam.Reliability.FixationLosses.Alert);
            Assert.That(exam.Reliability.LowReliability);
        }

        [Test]
        public void BlindSpot_IsRemapped_AfterConsecutiveFixationLosses()
        {
            int after = TestEnv.Config.Reliability.BlindSpot.RemapAfterConsecutiveLosses;
            Assert.That(after, Is.EqualTo(2), "clinical default agreed with the product owner");
            int remapped = 0;
            for (ulong seed = 1; seed <= 12; seed++)
            {
                var (exam, _) = TestEnv.Simulate(seed, obs: new ObserverParameters { FixationLossRate = 0.5 });
                var rel = exam.Reliability;
                int run = 0, maxRun = 0, searches = 0;
                foreach (var p in exam.Presentations)
                {
                    if (p.Request.Kind == PresentationKind.BlindSpotCatch) { run = p.Result.Responded ? run + 1 : 0; maxRun = Math.Max(maxRun, run); }
                    else if (p.Request.Kind == PresentationKind.BlindSpotMapping)
                    {
                        if (run > 0) searches++; // a new search right after losses
                        run = 0;
                        Assert.That(p.Disposition, Is.EqualTo(searches > 0 ? "remapping" : "mapping"));
                    }
                }
                Assert.That(maxRun, Is.LessThanOrEqualTo(after), $"seed {seed}: losses counted at one point before a new search");
                Assert.That(rel.BlindSpotRemaps, Is.EqualTo(searches), $"seed {seed}");
                if (rel.BlindSpotRemaps > 0) remapped++;
            }
            Assert.That(remapped, Is.GreaterThan(0), "a patient with 50% fixation losses triggers the remap in some exams");
            var (normal, _) = TestEnv.Simulate(5);
            Assert.That(normal.Reliability.BlindSpotRemaps, Is.EqualTo(0), "the normal observer never triggers it");
        }

        [Test]
        public void TriggerHappyObserver_RaisesFalsePositives_AndBlindSpotIsNotFound()
        {
            var (exam, _) = TestEnv.Simulate(7, obs: new ObserverParameters { FalsePositiveRate = 0.6 });
            Assert.That(exam.Reliability.FalsePositives.Alert);
            Assert.That(exam.Reliability.FalsePositives.Rate, Is.GreaterThan(0.3));
        }

        [Test]
        public void ObserverWhoSeesEverything_BlindSpotNotFound_HkDisabled()
        {
            var (exam, _) = TestEnv.Simulate(8, obs: new ObserverParameters { BlindSpotRadiusX = 0, BlindSpotRadiusY = 0 });
            Assert.That(exam.Reliability.BlindSpotFound, Is.False);
            Assert.That(exam.Presentations.Count(p => p.Request.Kind == PresentationKind.BlindSpotCatch), Is.EqualTo(0));
            Assert.That(exam.Presentations.Count(p => p.Request.Kind == PresentationKind.BlindSpotMapping), Is.EqualTo(13));
        }

        [Test]
        public void InattentiveObserver_RaisesFalseNegatives()
        {
            var (exam, _) = TestEnv.Simulate(9, obs: new ObserverParameters { FalseNegativeRate = 0.5 });
            Assert.That(exam.Reliability.FalseNegatives.Trials, Is.GreaterThan(0));
            Assert.That(exam.Reliability.FalseNegatives.Rate, Is.GreaterThan(0.25));
        }

        [Test]
        public void UnmonitoredMode_HasNoBlindSpotPresentations()
        {
            var (exam, _) = TestEnv.Simulate(10, fixation: FixationMode.CentralUnmonitored);
            Assert.That(exam.Presentations.Any(p => p.Request.Kind == PresentationKind.BlindSpotMapping || p.Request.Kind == PresentationKind.BlindSpotCatch), Is.False);
        }

        private static ExamSession NewSession(ulong seed = 1, FixationMode mode = FixationMode.CentralEyeTracking) =>
            new ExamSession(new ExamSettings { Seed = seed, FixationMode = mode, FalsePositiveCatchTrials = false, FalseNegativeCatchTrials = false },
                TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);

        [Test]
        public void GazeRejectedAndAnticipatedResponses_DoNotUpdateTheStrategy()
        {
            var s = NewSession();
            var r1 = s.Next();
            int loc = r1.LocationIndex;
            s.Submit(new PresentationResult { Sequence = r1.Sequence, Responded = true, LatencyMs = 400, FixationOk = false });
            Assert.That(s.Strategy.ProcedureOf(loc).NumPresentations, Is.EqualTo(0));
            var r2 = s.Next();
            s.Submit(new PresentationResult { Sequence = r2.Sequence, Responded = true, LatencyMs = 50 });
            Assert.That(s.Strategy.ProcedureOf(r2.LocationIndex).NumPresentations, Is.EqualTo(0));
            var rec = s.BuildRecord();
            Assert.That(rec.Reliability.GazeRejectedPresentations, Is.EqualTo(1));
            Assert.That(rec.Reliability.AnticipatedResponses, Is.EqualTo(1));
            Assert.That(rec.Presentations.Select(p => p.Disposition), Is.EqualTo(new[] { "fixationLost", "anticipated" }));
        }

        [Test]
        public void LateResponses_CountAsNotSeen()
        {
            var s = NewSession();
            var r = s.Next();
            s.Submit(new PresentationResult { Sequence = r.Sequence, Responded = true, LatencyMs = 1500 });
            var z = (ZestProcedure)s.Strategy.ProcedureOf(r.LocationIndex);
            Assert.That(z.NumPresentations, Is.EqualTo(1));
            Assert.That(z.Mean(), Is.LessThan(r.Db), "a not-seen moves the estimate brighter (lower dB)");
        }

        [Test]
        public void Session_ProtocolErrors()
        {
            var s = NewSession();
            var r = s.Next();
            Assert.Throws<InvalidOperationException>(() => s.Next());
            Assert.Throws<InvalidOperationException>(() => s.Submit(new PresentationResult { Sequence = r.Sequence + 1 }));
            var otherNorm = TestEnv.Norm("10-2");
            Assert.Throws<ArgumentException>(() => new ExamSession(new ExamSettings { GridId = "24-2" }, TestEnv.Config, otherNorm, TestEnv.Scale));
        }

        [Test]
        public void FinishedSession_ReturnsNull()
        {
            var s = NewSession(3, FixationMode.CentralUnmonitored);
            int n = 0;
            while (!s.IsFinished)
            {
                var r = s.Next();
                s.Submit(new PresentationResult { Sequence = r.Sequence, Responded = r.Db <= 12, LatencyMs = 400 });
                n++;
            }
            Assert.That(s.Next(), Is.Null);
            Assert.That(n, Is.GreaterThan(54));
        }

        [Test]
        public void CatchCounts()
        {
            var c = new CatchCount { Trials = 10, Errors = 2, AlertLimit = 0.15 };
            Assert.That(c.Rate, Is.EqualTo(0.2));
            Assert.That(c.Alert);
            Assert.That(c.ToString(), Is.EqualTo("2/10"));
            Assert.That(double.IsNaN(new CatchCount().Rate));
            Assert.That(new CatchCount().Alert, Is.False);
        }
    }
}
