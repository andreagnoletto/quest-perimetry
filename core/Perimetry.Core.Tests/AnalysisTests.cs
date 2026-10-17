using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Localization;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    public class AnalysisTests
    {
        [Test]
        public void GeneralHeight_IsSeventhBestOf52()
        {
            var td = Enumerable.Range(0, 54).Select(i => (double)i).ToArray(); // values 0..53
            var idx = Enumerable.Range(0, 52).ToArray();                       // 0..51 used
            Assert.That(GlobalIndices.GeneralHeight(td, idx, 0.85), Is.EqualTo(45)); // 51,50,...,45 = 7th
        }

        [Test]
        public void MeanDeviation_InverseVarianceAndInverseSd()
        {
            var td = new double[] { -2, -6 };
            var sd = new double[] { 1, 2 };
            var idx = new[] { 0, 1 };
            Assert.That(GlobalIndices.MeanDeviation(td, sd, idx, "inverseVariance"), Is.EqualTo((-2 * 1 + -6 * 0.25) / 1.25).Within(1e-12));
            Assert.That(GlobalIndices.MeanDeviation(td, sd, idx, "inverseSd"), Is.EqualTo((-2 * 1 + -6 * 0.5) / 1.5).Within(1e-12));
        }

        [Test]
        public void Psd_IsZeroForUniformDepression_AndHeijlFormulaOtherwise()
        {
            var sd = new double[] { 1, 2, 1.5 };
            var idx = new[] { 0, 1, 2 };
            var uniform = new double[] { -5, -5, -5 };
            double md = GlobalIndices.MeanDeviation(uniform, sd, idx, "inverseVariance");
            Assert.That(md, Is.EqualTo(-5).Within(1e-12));
            Assert.That(GlobalIndices.PatternStandardDeviation(uniform, sd, idx, md), Is.EqualTo(0).Within(1e-12));
            var td = new double[] { 0, -4, -1 };
            md = GlobalIndices.MeanDeviation(td, sd, idx, "inverseVariance");
            double expected = Math.Sqrt((1 + 4 + 2.25) / 3 * ((0 - md) * (0 - md) / 1 + (-4 - md) * (-4 - md) / 4 + (-1 - md) * (-1 - md) / 2.25) / 2);
            Assert.That(GlobalIndices.PatternStandardDeviation(td, sd, idx, md), Is.EqualTo(expected).Within(1e-12));
        }

        [Test]
        public void GhtPointScores()
        {
            var s = TestEnv.Config.Ght.PointScores;
            Assert.That(GlobalIndices.GhtPointScore(1, s), Is.EqualTo(0));
            Assert.That(GlobalIndices.GhtPointScore(0.05, s), Is.EqualTo(2));
            Assert.That(GlobalIndices.GhtPointScore(0.02, s), Is.EqualTo(5));
            Assert.That(GlobalIndices.GhtPointScore(0.01, s), Is.EqualTo(10));
            Assert.That(GlobalIndices.GhtPointScore(0.005, s), Is.EqualTo(20));
        }

        [Test]
        public void GhtSectors_ResolveToMirrorImagePoints()
        {
            var g = TestEnv.Grid();
            var sectors = GlobalIndices.ResolveSectors(TestEnv.Config.Ght, g);
            Assert.That(sectors.Count, Is.EqualTo(5));
            foreach (var (up, lo) in sectors)
            {
                Assert.That(up.Length, Is.EqualTo(lo.Length));
                Assert.That(up.All(i => g.Locations[i].Point.Y > 0));
                Assert.That(lo.All(i => g.Locations[i].Point.Y < 0));
                Assert.That(up.Concat(lo).Any(i => g.Locations[i].IsBlindSpot), Is.False);
            }
            Assert.That(sectors.Sum(s => s.upper.Length), Is.EqualTo(22));
        }

        [Test]
        public void Grayscale_TenBands()
        {
            var c = TestEnv.Config.Grayscale;
            Assert.That(Grayscale.Band(-3, c), Is.EqualTo(0));
            Assert.That(Grayscale.Band(0, c), Is.EqualTo(0));
            Assert.That(Grayscale.Band(17, c), Is.EqualTo(4));
            Assert.That(Grayscale.Band(36, c), Is.EqualTo(9));
            Assert.That(Grayscale.Band(40, c), Is.EqualTo(9));
            Assert.That(c.EdgesDb.Count + 1, Is.EqualTo(10));
        }

        [Test]
        public void ProvisionalNormative_HillOfVision_AgeDecline_Provisional()
        {
            var n = (NormativeDatabase)TestEnv.Norm();
            var g = TestEnv.Grid();
            Assert.That(n.IsProvisional);
            Assert.That(n.Id, Does.Contain("provisional"));
            Assert.That(n.LocationCount, Is.EqualTo(54));
            Assert.That(n.PopulationSize, Is.EqualTo(TestEnv.Config.ProvisionalNormative.Population.Size));
            int c = g.IndexOf(new FieldPoint(3, -3)).Value, p = g.IndexOf(new FieldPoint(-27, -3)).Value;
            Assert.That(n.ExpectedHfa(c, 50), Is.GreaterThan(n.ExpectedHfa(p, 50)), "hill of vision");
            double decade = n.ExpectedHfa(c, 40) - n.ExpectedHfa(c, 50);
            Assert.That(decade, Is.EqualTo(0.7).Within(0.15), "about 0.7 dB per decade");
            Assert.That(n.TdSd(p), Is.GreaterThan(n.TdSd(c)), "more variable in the periphery");
            Assert.That(n.PdSd(c), Is.GreaterThan(0));
            int bs = g.IndexOf(new FieldPoint(15, 3)).Value;
            Assert.That(double.IsNaN(n.TdSd(bs)));
            Assert.That(n.TdProbability(bs, -30), Is.EqualTo(1));
            Assert.That(n.PdProbability(bs, -30), Is.EqualTo(1));
            Assert.That(n.TdLut[c], Is.Ordered.Ascending);
            Assert.That(n.TdProbability(c, -50), Is.EqualTo(0.005));
            Assert.That(n.TdProbability(c, 0), Is.EqualTo(1));
            Assert.That(n.MdProbability(-10), Is.EqualTo(0.005));
            Assert.That(n.MdProbability(0), Is.EqualTo(1));
            Assert.That(n.PsdProbability(10), Is.EqualTo(0.005));
            Assert.That(n.PsdProbability(1), Is.EqualTo(1));
            Assert.That(n.GhQuantile(0.005), Is.LessThan(n.GhQuantile(0.995)));
            Assert.That(n.GhtSectorDiffQuantile(0, 0.99), Is.GreaterThanOrEqualTo(n.GhtSectorDiffQuantile(0, 0.97)));
        }

        [Test]
        public void NormativeBuilder_RecoversKnownAgeModel()
        {
            var g = TestEnv.Grid();
            var rng = new Rng(9);
            var pop = Enumerable.Range(0, 400).Select(k =>
            {
                double age = rng.Uniform(20, 80);
                return new NormalSubject { Age = age, Sensitivity = g.Locations.Select(l => 35 - 0.1 * age + rng.Gaussian(0, 1)).ToArray() };
            }).ToList();
            var db = NormativeBuilder.Build("test", false, g, pop, TestEnv.Config.Analysis, TestEnv.Config.Ght);
            Assert.That(db.IsProvisional, Is.False);
            Assert.That(db.Slope.Average(), Is.EqualTo(-0.1).Within(0.01));
            Assert.That(db.Intercept.Average(), Is.EqualTo(35).Within(0.5));
            Assert.That(db.TdSdByLocation.Where(v => !double.IsNaN(v)).Average(), Is.EqualTo(1).Within(0.1));
            Assert.Throws<ArgumentException>(() => NormativeBuilder.Build("x", false, g, pop.Take(10).ToList(), TestEnv.Config.Analysis, TestEnv.Config.Ght));
            Assert.Throws<ArgumentException>(() => NormativeBuilder.Build("x", false, g,
                pop.Select(p => new NormalSubject { Age = p.Age, Sensitivity = new double[3] }).ToList(), TestEnv.Config.Analysis, TestEnv.Config.Ght));
        }

        [Test]
        public void ProbabilityLevels_AreCalibratedOnTheNormalPopulation()
        {
            // About 5% of normal (synthetic) locations should be flagged p < 5% on TD
            var n = TestEnv.Norm();
            var g = TestEnv.Grid();
            var pop = ProvisionalNormative.GeneratePopulation(TestEnv.Config.ProvisionalNormative, g).Take(500).ToList();
            int flagged = 0, total = 0;
            foreach (var s in pop)
                foreach (var l in g.AnalysisLocations)
                {
                    total++;
                    if (n.TdProbability(l.Index, s.Sensitivity[l.Index] - n.ExpectedHfa(l.Index, s.Age)) <= 0.05) flagged++;
                }
            Assert.That(flagged / (double)total, Is.EqualTo(0.05).Within(0.01));
        }

        [Test]
        public void NormalField_IsWithinNormalLimits()
        {
            int wnl = 0, runs = 10;
            for (int k = 0; k < runs; k++)
            {
                var (exam, _) = TestEnv.Simulate((ulong)(100 + k));
                var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
                if (a.Ght == GhtResult.WithinNormalLimits) wnl++;
                Assert.That(a.Vfi, Is.GreaterThan(95));
                Assert.That(a.Md, Is.GreaterThan(-3));
            }
            Assert.That(wnl, Is.GreaterThanOrEqualTo(8));
        }

        [Test]
        public void SuperiorArcuate_IsOutsideNormalLimits()
        {
            for (int k = 0; k < 5; k++)
            {
                var (exam, _) = TestEnv.Simulate((ulong)(200 + k), defect: DefectType.SuperiorArcuate);
                var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
                Assert.That(a.Ght, Is.EqualTo(GhtResult.OutsideNormalLimits));
                Assert.That(a.PsdP, Is.LessThanOrEqualTo(0.01));
                Assert.That(a.Vfi, Is.LessThan(90));
            }
        }

        [Test]
        public void DiffuseDepression_IsGeneralReduction()
        {
            var cfg = TestEnv.Config;
            var g = TestEnv.Grid();
            var rng = new Rng(77);
            var field = SyntheticField.Create(cfg.ProvisionalNormative, g, 50, DefectType.Normal, 0, rng, globalShiftDb: -7);
            var obs = new SimulatedObserver(field, TestEnv.Scale, Eye.OD, new ObserverParameters(), rng.Fork());
            var exam = ExamSimulator.Run(new ExamSession(new ExamSettings { Seed = 77, Strategy = StrategyKind.ZestFast }, cfg, TestEnv.Norm(), TestEnv.Scale), obs);
            var a = FieldAnalysis.Analyze(exam, cfg, TestEnv.Norm());
            Assert.That(a.Ght, Is.EqualTo(GhtResult.GeneralReduction).Or.EqualTo(GhtResult.BorderlineAndGeneralReduction));
            Assert.That(a.MdP, Is.LessThanOrEqualTo(0.01));
            Assert.That(a.PsdP, Is.EqualTo(1));
        }

        [Test]
        public void ClassifyGht_AllBranches()
        {
            var n = TestEnv.Norm();
            var ght = TestEnv.Config.Ght;
            var zero = new double[5];
            double ghMid = n.GhQuantile(0.5);
            Assert.That(FieldAnalysis.ClassifyGht(zero, ghMid, n, ght), Is.EqualTo(GhtResult.WithinNormalLimits));
            Assert.That(FieldAnalysis.ClassifyGht(new double[] { 0, 0, 999, 0, 0 }, ghMid, n, ght), Is.EqualTo(GhtResult.OutsideNormalLimits));
            Assert.That(FieldAnalysis.ClassifyGht(zero, -50, n, ght), Is.EqualTo(GhtResult.GeneralReduction));
            Assert.That(FieldAnalysis.ClassifyGht(zero, 50, n, ght), Is.EqualTo(GhtResult.AbnormallyHigh));
            // borderline: between the 97th and 99th percentile in some sector
            for (int s = 0; s < 5; s++)
            {
                double q97 = n.GhtSectorDiffQuantile(s, ght.BorderlinePercentile), q99 = n.GhtSectorDiffQuantile(s, ght.OutsideNormalLimitsPercentile);
                if (q99 > q97)
                {
                    var d = new double[5]; d[s] = (q97 + q99) / 2;
                    Assert.That(FieldAnalysis.ClassifyGht(d, ghMid, n, ght), Is.EqualTo(GhtResult.Borderline));
                    Assert.That(FieldAnalysis.ClassifyGht(d, -50, n, ght), Is.EqualTo(GhtResult.BorderlineAndGeneralReduction));
                    return;
                }
            }
            Assert.Inconclusive("no sector with distinct 97/99 percentiles");
        }

        [Test]
        public void Vfi_IsHundredWhenNothingSignificant_AndLowerWithDefects()
        {
            var g = TestEnv.Grid();
            var idx = g.AnalysisLocations.Select(l => l.Index).ToArray();
            int n = g.Count;
            var normal = Enumerable.Repeat(30.0, n).ToArray();
            var ns = Enumerable.Repeat(1.0, n).ToArray();
            var td = new double[n];
            var cfg = TestEnv.Config.Analysis.Vfi;
            Assert.That(GlobalIndices.Vfi(g, idx, 0, normal, normal, td, ns, ns, cfg), Is.EqualTo(100).Within(1e-9));
            var sens = normal.ToArray(); var pdP = ns.ToArray();
            int c = g.IndexOf(new FieldPoint(3, 3)).Value;
            sens[c] = 0; td[c] = -30; pdP[c] = 0.005;
            double vfi = GlobalIndices.Vfi(g, idx, -1, sens, normal, td, ns, pdP, cfg);
            Assert.That(vfi, Is.LessThan(100));
            sens[c] = -1;
            Assert.That(GlobalIndices.Vfi(g, idx, -1, sens, normal, td, ns, pdP, cfg), Is.EqualTo(vfi).Within(1e-9));
            sens[c] = 0;
            // MD < -20 dB -> the TD map decides
            Assert.That(GlobalIndices.Vfi(g, idx, -25, sens, normal, td, ns, pdP, cfg), Is.EqualTo(100).Within(1e-9));
        }

        [Test]
        public void Analysis_CarriesWarnings_AndHfaEquivalentValues()
        {
            var (exam, _) = TestEnv.Simulate(31, obs: new ObserverParameters { FalsePositiveRate = 0.5, FalseNegativeRate = 0.5 });
            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.Warnings, Has.Some.Contains("ANVISA"));
            Assert.That(a.Warnings, Has.Some.Contains("NÃO CALIBRADO"));
            Assert.That(a.Warnings, Has.Some.Contains("PROVISÓRIO"));
            Assert.That(a.Warnings, Has.Some.Contains("Falsos positivos"));
            foreach (var p in a.Points)
                Assert.That(p.HfaEqDb, Is.EqualTo(Math.Max(0, p.DeviceDb) + exam.HfaOffsetDb).Within(1e-9));
            Assert.That(a.Points.Count, Is.EqualTo(54));
            Assert.That(a.NormativeProvisional);
            Assert.That(a.Calibrated, Is.False);
            Assert.Throws<ArgumentException>(() => FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm("10-2")));
        }

        [Test]
        public void Analysis_WarnsWhenBlindSpotNotFound()
        {
            var (exam, _) = TestEnv.Simulate(32, obs: new ObserverParameters { BlindSpotRadiusX = 0, BlindSpotRadiusY = 0 });
            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.Warnings, Has.Some.Contains("Mancha cega não localizada"));
        }

        [Test]
        public void ScreeningExam_HasOutcomesButNoIndices()
        {
            var (exam, field) = TestEnv.Simulate(33, strategy: StrategyKind.Screening, defect: DefectType.SuperiorArcuate);
            var a = FieldAnalysis.Analyze(exam, TestEnv.Config, TestEnv.Norm());
            Assert.That(a.Ght, Is.EqualTo(GhtResult.NotApplicable));
            Assert.That(double.IsNaN(a.Md));
            var defect = a.Points.Where(p => field.InDefect[p.Index]).ToList();
            Assert.That(defect.Count(p => p.ScreeningOutcome == "NotSeen") / (double)defect.Count, Is.GreaterThan(0.7));
            var normal = a.Points.Where(p => !field.InDefect[p.Index] && !p.IsBlindSpot).ToList();
            Assert.That(normal.Count(p => p.ScreeningOutcome == "NotSeen"), Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Localization()
        {
            Assert.That(Strings.Ght(GhtResult.OutsideNormalLimits), Is.EqualTo("Fora dos limites normais"));
            Assert.That(Strings.Ght(GhtResult.OutsideNormalLimits, Strings.EnUs), Is.EqualTo("Outside normal limits"));
            Assert.That(Strings.Get("examDone"), Is.EqualTo("Exame concluído. Aguarde o médico."));
            Assert.That(Strings.Get("missing.key"), Is.EqualTo("missing.key"));
            foreach (GhtResult r in Enum.GetValues(typeof(GhtResult)))
                Assert.That(Strings.Ght(r), Is.Not.EqualTo("ght." + r));
        }
    }
}
