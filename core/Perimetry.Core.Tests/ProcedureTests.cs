using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    public class ProcedureTests
    {
        private static List<(double stim, bool seen)> RunDeterministic(ILocationProcedure p, double threshold, int max = 200)
        {
            var log = new List<(double, bool)>();
            while (!p.IsFinished && log.Count < max)
            {
                double s = p.NextStimulusDb();
                bool seen = s <= threshold;
                p.Update(s, seen);
                log.Add((s, seen));
            }
            return log;
        }

        private static ZestProcedure Zest(double[] prior = null, string stopType = "S", double stopValue = 1.5, string choice = "mean", double min = 0, double max = 40)
        {
            var d = ZestProcedure.MakeDomain(0, 40, 1);
            var lik = ZestProcedure.GaussianLikelihood(d, 0.03, 0.03, _ => 1.0);
            return new ZestProcedure(d, prior ?? d.Select(_ => 1.0).ToArray(), lik, stopType, stopValue, min, max, 2, 2, 100, choice);
        }

        [Test]
        public void Zest_UniformPrior_FirstStimulusIsDomainMean()
        {
            var z = Zest();
            Assert.That(z.NextStimulusDb(), Is.EqualTo(20));
            Assert.That(z.Mean(), Is.EqualTo(20).Within(1e-12));
            Assert.That(z.Entropy(), Is.EqualTo(Math.Log(41, 2)).Within(1e-9));
            Assert.That(z.StopReason, Is.EqualTo("Not"));
        }

        [TestCase(5.0)]
        [TestCase(18.0)]
        [TestCase(31.0)]
        public void Zest_ConvergesToDeterministicThreshold(double t)
        {
            var z = Zest();
            RunDeterministic(z, t);
            Assert.That(z.IsFinished);
            Assert.That(z.Stdev(), Is.LessThanOrEqualTo(1.5));
            Assert.That(z.FinalDb, Is.EqualTo(t + 0.5).Within(1.5));
            Assert.That(z.StopReason, Is.EqualTo("Stop"));
        }

        [Test]
        public void Zest_SeenTwiceAtDimmest_StopsMax_NotSeenTwiceAtBrightest_StopsMin()
        {
            var hi = Zest(max: 30);
            RunDeterministic(hi, 100);
            Assert.That(hi.StopReason, Is.EqualTo("Max"));
            Assert.That(hi.IsCensoredHigh);
            var d = ZestProcedure.MakeDomain(-5, 40, 1);
            var lo = new ZestProcedure(d, d.Select(_ => 1.0).ToArray(), ZestProcedure.GaussianLikelihood(d, 0.03, 0.03, _ => 1.0), "S", 1.5, 0, 40);
            RunDeterministic(lo, -100);
            Assert.That(lo.StopReason, Is.EqualTo("Min"));
            Assert.That(lo.IsCensoredLow);
            Assert.That(lo.CurrNotSeenLimit, Is.EqualTo(2));
        }

        [Test]
        public void Zest_OtherStopRulesAndStimChoices()
        {
            var n = Zest(stopType: "N", stopValue: 4);
            Assert.That(RunDeterministic(n, 20).Count, Is.EqualTo(4));
            Assert.That(n.StopReason, Is.EqualTo("Stop"));
            var h = Zest(stopType: "H", stopValue: 3);
            RunDeterministic(h, 20);
            Assert.That(h.Entropy(), Is.LessThanOrEqualTo(3));
            foreach (var c in new[] { "mode", "median" })
            {
                var z = Zest(choice: c);
                RunDeterministic(z, 12);
                Assert.That(z.FinalDb, Is.EqualTo(12).Within(2), c);
            }
            var mp = new ZestProcedure(new double[] { 0, 1 }, new double[] { 1, 1 }, new double[2, 2], "N", 1000, maxPresentations: 3);
            for (int i = 0; i < 3; i++) mp.Update(mp.NextStimulusDb(), i % 2 == 0);
            Assert.That(mp.StopReason, Is.EqualTo("MaxPresentations"));
        }

        [Test]
        public void Zest_InvalidArguments()
        {
            var d = ZestProcedure.MakeDomain(0, 10, 1);
            var l = ZestProcedure.GaussianLikelihood(d, 0.03, 0.03, _ => 1);
            var p = d.Select(_ => 1.0).ToArray();
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, p, l, "X"));
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, p, new double[3, 3]));
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, new double[2], l));
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, p, l, stimChoice: "max"));
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, p, l, minStimulus: 0.5));
            Assert.Throws<ArgumentException>(() => new ZestProcedure(d, p, l, maxStimulus: 11));
            var z = new ZestProcedure(d, p, l);
            Assert.Throws<ArgumentException>(() => z.Update(3.5, true));
        }

        [Test]
        public void Zest_Likelihood_IsOpiFormula()
        {
            var d = new double[] { 10, 20 };
            var l = ZestProcedure.GaussianLikelihood(d, 0.03, 0.03, _ => 1);
            // likelihood[stim = 10, threshold = 20] = 0.03 + 0.94 * (1 - pnorm(10, 20, 1))
            Assert.That(l[0, 1], Is.EqualTo(0.03 + 0.94 * (1 - NormalDistribution.Cdf(10, 20, 1))).Within(1e-15));
            Assert.That(l[1, 1], Is.EqualTo(0.03 + 0.94 * 0.5).Within(1e-15));
        }

        [Test]
        public void FourTwo_KnownSequence()
        {
            // start 25, threshold 20: 25- 21- 17+ (rev) 19+ 21- (rev) -> mean(19, 21) = 20
            var p = new FourTwoProcedure(25, 0, 40);
            var log = RunDeterministic(p, 20);
            Assert.That(log.Select(l => l.stim), Is.EqualTo(new double[] { 25, 21, 17, 19, 21 }));
            Assert.That(p.FinalDb, Is.EqualTo(20));
            Assert.That(p.StopReason, Is.EqualTo("Rev"));
            Assert.Throws<InvalidOperationException>(() => p.Update(20, true));
            Assert.Throws<ArgumentException>(() => new FourTwoProcedure(41, 0, 40));
        }

        [Test]
        public void FourTwo_Limits()
        {
            var p = new FourTwoProcedure(4, 0, 40);
            RunDeterministic(p, -10);
            Assert.That(p.StopReason, Is.EqualTo("Min"));
            Assert.That(p.FinalDb, Is.EqualTo(0));
            Assert.That(p.IsCensoredLow);
            var q = new FourTwoProcedure(36, 0, 40);
            RunDeterministic(q, 99);
            Assert.That(q.StopReason, Is.EqualTo("Max"));
            Assert.That(q.IsCensoredHigh);
        }

        [Test]
        public void FullThreshold_LastSeen_AndSecondStaircaseWhenFarFromStart()
        {
            // first staircase: 25- 21- 17+ 19+ 21- -> last seen 19; |19 - 25| > 4 -> second staircase from 19.
            // OPI keeps lastResponse across staircases, so 19+ is already a reversal; 21- ends it -> 19.
            var p = new FullThresholdProcedure(25, 0, 40);
            var log = RunDeterministic(p, 20);
            Assert.That(log.Select(l => l.stim), Is.EqualTo(new double[] { 25, 21, 17, 19, 21, 19, 21 }));
            Assert.That(p.FirstStairResult, Is.EqualTo(19));
            Assert.That(p.RanSecondStaircase);
            Assert.That(p.FinalDb, Is.EqualTo(19));
            Assert.That(p.NumPresentations, Is.EqualTo(7));
        }

        [Test]
        public void FullThreshold_NoSecondStaircaseWhenCloseToStart()
        {
            var p = new FullThresholdProcedure(21, 0, 40);
            var log = RunDeterministic(p, 20);
            // 21- 17+ (rev) 19+ 21- (rev) -> last seen 19, |19-21| <= 4
            Assert.That(log.Select(l => l.stim), Is.EqualTo(new double[] { 21, 17, 19, 21 }));
            Assert.That(p.FinalDb, Is.EqualTo(19));
            Assert.That(p.RanSecondStaircase, Is.False);
            Assert.That(p.StopReason, Is.EqualTo("Reversals"));
            Assert.Throws<InvalidOperationException>(() => p.Update(19, true));
            Assert.That(double.IsNaN(new FullThresholdProcedure(10, 0, 40).FinalDb));
            Assert.Throws<ArgumentException>(() => new FullThresholdProcedure(-1, 0, 40));
        }

        [Test]
        public void FullThreshold_CensoredBothEnds()
        {
            var lo = new FullThresholdProcedure(4, 0, 40);
            RunDeterministic(lo, -5);
            Assert.That(lo.IsCensoredLow);
            Assert.That(lo.FinalDb, Is.EqualTo(0));
            var hi = new FullThresholdProcedure(36, 0, 40);
            RunDeterministic(hi, 99);
            Assert.That(hi.IsCensoredHigh);
            Assert.That(hi.FinalDb, Is.EqualTo(40));
            var off = new FullThresholdProcedure(25, 0, 40, useSecondStaircase: false);
            RunDeterministic(off, 20);
            Assert.That(off.RanSecondStaircase, Is.False);
            Assert.That(off.FinalDb, Is.EqualTo(19));
        }

        [Test]
        public void Screening_SeenRetestNotSeen()
        {
            var a = new ScreeningProcedure(10, true);
            a.Update(10, true);
            Assert.That(a.Outcome, Is.EqualTo(ScreeningOutcome.Seen));
            Assert.Throws<InvalidOperationException>(() => a.Update(10, true));
            var b = new ScreeningProcedure(10, true);
            b.Update(10, false);
            Assert.That(b.IsFinished, Is.False);
            b.Update(10, true);
            Assert.That(b.Outcome, Is.EqualTo(ScreeningOutcome.SeenOnRetest));
            var c = new ScreeningProcedure(10, true);
            c.Update(10, false); c.Update(10, false);
            Assert.That(c.Outcome, Is.EqualTo(ScreeningOutcome.NotSeen));
            var d = new ScreeningProcedure(10, false);
            d.Update(10, false);
            Assert.That(d.Outcome, Is.EqualTo(ScreeningOutcome.NotSeen));
            Assert.That(double.IsNaN(d.FinalDb));
            Assert.That(d.IsCensoredLow || d.IsCensoredHigh, Is.False);
            Assert.That(d.StopReason, Is.EqualTo("NotSeen"));
        }

        [Test]
        public void Factory_BuildsEveryStrategyFromConfig()
        {
            var cfg = TestEnv.Config;
            foreach (StrategyKind k in Enum.GetValues(typeof(StrategyKind)))
            {
                var f = new ProcedureFactory(k, cfg.Strategies, TestEnv.Scale);
                var p = f.Create(14.6);
                Assert.That(p.IsFinished, Is.False, k.ToString());
                double s = p.NextStimulusDb();
                Assert.That(s, Is.InRange(cfg.Strategies.MinStimulusDb, cfg.Strategies.MaxStimulusDb), k.ToString());
                if (k == StrategyKind.Screening) Assert.That(s, Is.EqualTo(Math.Round(14.6 - cfg.Strategies.Screening.BrighterThanExpectedByDb)));
                if (k == StrategyKind.FullThreshold || k == StrategyKind.FourTwo) Assert.That(s, Is.EqualTo(15));
            }
        }

        [Test]
        public void ZestPrior_IsBimodal_WithNormalModeAtExpected()
        {
            var f = new ProcedureFactory(StrategyKind.Zest, TestEnv.Config.Strategies, TestEnv.Scale);
            var prior = f.ZestPrior(15);
            var d = ZestProcedure.MakeDomain(TestEnv.Config.Strategies.Zest.DomainMinDb, TestEnv.Config.Strategies.Zest.DomainMaxDb, 1);
            int mode = Array.IndexOf(prior, prior.Max());
            Assert.That(d[mode], Is.EqualTo(15));
            Assert.That(prior.All(p => p > 0));
            // abnormal component: more mass at the bottom of the domain than a single Gaussian would give
            Assert.That(prior[0] / prior.Max(), Is.GreaterThan(0.05));
        }

        [Test]
        public void HensonLikelihoodOption()
        {
            var s = Newtonsoft.Json.JsonConvert.DeserializeObject<Config.StrategiesConfig>(Newtonsoft.Json.JsonConvert.SerializeObject(TestEnv.Config.Strategies));
            s.Zest.LikelihoodSd.Type = "henson";
            var f = new ProcedureFactory(StrategyKind.Zest, s, TestEnv.Scale);
            var p = f.Create(14);
            RunDeterministic(p, 14);
            Assert.That(p.FinalDb, Is.EqualTo(14.5).Within(2));
        }
    }
}
