using System;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    /// <summary>Phase 1 acceptance criteria from SPEC.md §11, measured in simulation.</summary>
    [Category("Acceptance")]
    public class AcceptanceTests
    {
        private const int Runs = 12;

        private static double InRangeMae(Perimetry.Core.Exam.ExamRecord exam, SyntheticField field)
        {
            var scale = TestEnv.Scale;
            var pts = exam.Locations.Where(l => !l.IsBlindSpot
                && scale.FromHfaEquivalent(field.TrueHfaDb[l.Index]) >= TestEnv.Config.Strategies.MinStimulusDb).ToList();
            return pts.Average(l => Math.Abs(l.ThresholdDb + exam.HfaOffsetDb - field.TrueHfaDb[l.Index]));
        }

        [TestCase(StrategyKind.ZestFast)]
        [TestCase(StrategyKind.Zest)]
        public void Zest_242_NormalObserver_MeanAbsoluteErrorBelow2Db(StrategyKind k)
        {
            var mae = Enumerable.Range(0, Runs).Select(i => { var (e, f) = TestEnv.Simulate((ulong)(500 + i), strategy: k); return InRangeMae(e, f); }).ToList();
            TestContext.WriteLine($"{k}: MAE mean {mae.Average():0.00} max {mae.Max():0.00}");
            Assert.That(mae.Average(), Is.LessThan(2.0));
        }

        [Test]
        public void ZestFast_242_NormalObserver_DurationBelow5Minutes()
        {
            var minutes = Enumerable.Range(0, Runs).Select(i => TestEnv.Simulate((ulong)(600 + i)).exam.Reliability.DurationMs / 60000).ToList();
            TestContext.WriteLine($"ZestFast duration mean {minutes.Average():0.00} max {minutes.Max():0.00} min");
            Assert.That(minutes.Average(), Is.LessThan(5.0));
        }

        [Test]
        public void ArcuateDefect_DetectedWithPatternDeviationP1Percent_AtTheRightPoints()
        {
            double hit = 0, falseHit = 0;
            for (int i = 0; i < Runs; i++)
            {
                var (exam, field) = TestEnv.Simulate((ulong)(700 + i), defect: DefectType.SuperiorArcuate);
                var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
                var def = a.Points.Where(p => !p.IsBlindSpot && field.InDefect[p.Index]).ToList();
                var rest = a.Points.Where(p => !p.IsBlindSpot && !field.InDefect[p.Index]).ToList();
                hit += def.Count(p => p.PdP <= 0.01) / (double)def.Count;
                falseHit += rest.Count(p => p.PdP <= 0.01) / (double)rest.Count;
            }
            TestContext.WriteLine($"defect points flagged {hit / Runs:P0}, other points flagged {falseHit / Runs:P0}");
            Assert.That(hit / Runs, Is.GreaterThan(0.9));
            Assert.That(falseHit / Runs, Is.LessThan(0.05));
        }

        [Test]
        public void FullThreshold_242_NormalObserver_IsAccurate()
        {
            var mae = Enumerable.Range(0, 6).Select(i => { var (e, f) = TestEnv.Simulate((ulong)(800 + i), strategy: StrategyKind.FullThreshold); return InRangeMae(e, f); }).ToList();
            Assert.That(mae.Average(), Is.LessThan(2.5));
        }

        [TestCase("30-2")]
        [TestCase("10-2")]
        [TestCase("24-2C")]
        public void OtherGrids_RunAndAreAccurate(string grid)
        {
            var (e, f) = TestEnv.Simulate(900, grid: grid);
            Assert.That(InRangeMae(e, f), Is.LessThan(2.5));
            var a = FieldAnalysis.Analyze(e, TestEnv.Config, TestEnv.Norm(grid));
            Assert.That(a.Points.Count, Is.EqualTo(TestEnv.Grid(grid).Count));
        }
    }
}
