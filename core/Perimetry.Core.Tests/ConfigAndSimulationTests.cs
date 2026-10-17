using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;

namespace Perimetry.Core.Tests
{
    public class ConfigAndSimulationTests
    {
        [Test]
        public void ConfigLoads_AndHashIsStable()
        {
            var c = TestEnv.Config;
            Assert.That(c.Display.BackgroundCdm2, Is.EqualTo(10));
            Assert.That(c.Stimulus.DurationMs, Is.EqualTo(200));
            Assert.That(c.Stimulus.DefaultSize, Is.EqualTo("III"));
            Assert.That(c.Timing.ResponseWindowMs, Is.EqualTo(1000));
            Assert.That(c.Reliability.FixationLoss.AlertLimit, Is.EqualTo(0.20));
            Assert.That(c.Reliability.FalsePositive.AlertLimit, Is.EqualTo(0.15));
            Assert.That(c.Reliability.FalseNegative.AlertLimit, Is.EqualTo(0.33));
            Assert.That(c.Reliability.FalseNegative.BrighterByDb, Is.EqualTo(9));
            Assert.That(c.Strategies.Zest.StopValue, Is.EqualTo(1.5));
            Assert.That(c.Grids.Count, Is.EqualTo(4));
            Assert.That(c.Hash, Has.Length.EqualTo(64));
            Assert.That(ClinicalConfig.ComputeHash(TestEnv.ConfigDir), Is.EqualTo(c.Hash));
        }

        [Test]
        public void ConfigHash_ChangesWhenAnyFileChanges()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "perimetry-cfg-" + Guid.NewGuid());
            CopyDir(TestEnv.ConfigDir, tmp);
            try
            {
                string before = ClinicalConfig.ComputeHash(tmp);
                File.AppendAllText(Path.Combine(tmp, "timing.json"), " ");
                Assert.That(ClinicalConfig.ComputeHash(tmp), Is.Not.EqualTo(before));
                Assert.That(ClinicalConfig.LoadFromDirectory(tmp).Timing.IsiMinMs, Is.EqualTo(600));
            }
            finally { Directory.Delete(tmp, true); }
        }

        [Test]
        public void ConfigErrors()
        {
            Assert.Throws<DirectoryNotFoundException>(() => ClinicalConfig.LoadFromDirectory("/nonexistent/dir"));
            Assert.Throws<DirectoryNotFoundException>(() => ClinicalConfig.FindConfigDirectory(Path.GetTempPath()));
        }

        private static void CopyDir(string src, string dst)
        {
            foreach (var d in Directory.GetDirectories(src, "*", SearchOption.AllDirectories)) Directory.CreateDirectory(d.Replace(src, dst));
            Directory.CreateDirectory(dst);
            foreach (var f in Directory.GetFiles(src, "*", SearchOption.AllDirectories)) File.Copy(f, f.Replace(src, dst));
        }

        private static SimulatedObserver Observer(ObserverParameters p, double trueHfa = 30)
        {
            var g = TestEnv.Grid();
            var field = SyntheticField.Create(TestEnv.Config.ProvisionalNormative, g, 50, DefectType.Normal, 0, new Rng(1), localNoiseScale: 0, globalShiftDb: 0);
            for (int i = 0; i < g.Count; i++) field.TrueHfaDb[i] = trueHfa;
            return new SimulatedObserver(field, TestEnv.Scale, Eye.OD, p, new Rng(2));
        }

        [Test]
        public void Observer_SeeingProbability_IsOpiSimHensonFormula()
        {
            var o = Observer(new ObserverParameters());
            Assert.That(o.SdAt(30), Is.EqualTo(Math.Min(6, Math.Exp(-0.081 * 30 + 3.27))).Within(1e-12));
            Assert.That(o.SdAt(-10), Is.EqualTo(6));
            double stimDevice = TestEnv.Scale.FromHfaEquivalent(30);
            Assert.That(o.ProbabilitySeen(stimDevice, new FieldPoint(3, 3)), Is.EqualTo(0.03 + 0.96 * 0.5).Within(1e-12));
            Assert.That(o.ProbabilitySeen(0, new FieldPoint(3, 3)), Is.GreaterThan(0.95));
            Assert.That(o.ProbabilitySeen(0, new FieldPoint(15, -1.5)), Is.EqualTo(0.03), "blind spot: false positives only");
            Assert.That(o.InBlindSpot(new FieldPoint(15, -4.5)));
            Assert.That(o.InBlindSpot(new FieldPoint(15, 3)), Is.False);
            Assert.That(o.TrueThresholdAt(new FieldPoint(4, 4)), Is.EqualTo(30));
            var g = Observer(new ObserverParameters { Model = PsychometricModel.Gaussian, GaussianSd = 2 });
            Assert.That(g.SdAt(10), Is.EqualTo(2));
        }

        [Test]
        public void Observer_Responses_FalsePositiveAndFixationLoss()
        {
            var always = Observer(new ObserverParameters { FalsePositiveRate = 1, FalseNegativeRate = 0 });
            var blank = new PresentationRequest { Sequence = 1, StimulusShown = false, ResponseWindowMs = 1000 };
            var r = always.Respond(blank, 123);
            Assert.That(r.Responded);
            Assert.That(r.LatencyMs, Is.InRange(150, 1000));
            Assert.That(r.OnsetMs, Is.EqualTo(123));
            var fl = Observer(new ObserverParameters { FalsePositiveRate = 0, FalseNegativeRate = 0, FixationLossRate = 1 });
            var bs = new PresentationRequest { Sequence = 2, StimulusShown = true, Db = 0, FieldX = 15, FieldY = -1.5, ResponseWindowMs = 1000 };
            Assert.That(fl.Respond(bs, 0).Responded, "fixation lost: blind spot stimulus falls on seeing retina");
            var never = Observer(new ObserverParameters { FalsePositiveRate = 0, FalseNegativeRate = 1 });
            var stim = new PresentationRequest { Sequence = 3, StimulusShown = true, Db = 0, FieldX = 3, FieldY = 3, ResponseWindowMs = 1000 };
            var nr = never.Respond(stim, 0);
            Assert.That(nr.Responded, Is.False);
            Assert.That(nr.LatencyMs, Is.Null);
        }

        [Test]
        public void SyntheticDefects_AffectTheRightRegions()
        {
            var g = TestEnv.Grid();
            Assert.That(SyntheticField.IsInDefect(DefectType.SuperiorArcuate, new FieldPoint(-9, 15)));
            Assert.That(SyntheticField.IsInDefect(DefectType.SuperiorArcuate, new FieldPoint(-9, -15)), Is.False);
            Assert.That(SyntheticField.IsInDefect(DefectType.InferiorArcuate, new FieldPoint(-9, -15)));
            Assert.That(SyntheticField.IsInDefect(DefectType.SuperiorArcuate, new FieldPoint(3, 3)), Is.False, "spares the centre");
            Assert.That(SyntheticField.IsInDefect(DefectType.Hemianopia, new FieldPoint(3, 3)));
            Assert.That(SyntheticField.IsInDefect(DefectType.Hemianopia, new FieldPoint(-3, 3)), Is.False);
            Assert.That(SyntheticField.IsInDefect(DefectType.CentralScotoma, new FieldPoint(3, 3)));
            Assert.That(SyntheticField.IsInDefect(DefectType.Normal, new FieldPoint(3, 3)), Is.False);
            var f = SyntheticField.Create(TestEnv.Config.ProvisionalNormative, g, 50, DefectType.SuperiorArcuate, 20, new Rng(4), localNoiseScale: 0, globalShiftDb: 0);
            int a = g.IndexOf(new FieldPoint(-9, 15)).Value, b = g.IndexOf(new FieldPoint(-9, -15)).Value;
            Assert.That(f.TrueHfaDb[b] - f.TrueHfaDb[a], Is.GreaterThan(18));
            Assert.That(f.InDefect.Count(x => x), Is.GreaterThan(10));
            Assert.That(f.Defect, Is.EqualTo(DefectType.SuperiorArcuate));
        }

        [Test]
        public void Simulator_GuardsAgainstNonTermination()
        {
            var g = TestEnv.Grid();
            var rng = new Rng(1);
            var field = SyntheticField.Create(TestEnv.Config.ProvisionalNormative, g, 50, DefectType.Normal, 0, rng);
            var obs = new SimulatedObserver(field, TestEnv.Scale, Eye.OD, new ObserverParameters(), rng.Fork());
            var session = new ExamSession(new ExamSettings(), TestEnv.Config, TestEnv.Norm(), TestEnv.Scale);
            Assert.Throws<InvalidOperationException>(() => ExamSimulator.Run(session, obs, maxPresentations: 10));
        }
    }
}
