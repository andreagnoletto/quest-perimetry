using System.IO;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Luminance;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Tests
{
    internal static class TestEnv
    {
        private static ClinicalConfig _cfg;
        public static ClinicalConfig Config => _cfg ??= ClinicalConfig.LoadFromDirectory(ConfigDir);
        public static string ConfigDir => ClinicalConfig.FindConfigDirectory(TestContext.CurrentContext.TestDirectory);
        public static DbScale Scale => DbScale.Nominal(Config.Display);
        public static TestGrid Grid(string id = "24-2") => GridLibrary.Get(Config, id);
        public static INormativeDatabase Norm(string id = "24-2") => ProvisionalNormative.For(Config, Grid(id));

        public static (ExamRecord exam, SyntheticField field) Simulate(ulong seed, string grid = "24-2", StrategyKind strategy = StrategyKind.ZestFast,
            DefectType defect = DefectType.Normal, double depth = 15, Eye eye = Eye.OD, double age = 50, ObserverParameters obs = null,
            FixationMode fixation = FixationMode.CentralHeijlKrakau)
        {
            var settings = new ExamSettings { GridId = grid, Strategy = strategy, Eye = eye, AgeYears = age, Seed = seed, FixationMode = fixation };
            var rng = new Rng(seed * 7919 + 17);
            var field = SyntheticField.Create(Config.ProvisionalNormative, Grid(grid), age, defect, depth, rng);
            var observer = new SimulatedObserver(field, Scale, eye, obs ?? new ObserverParameters(), rng.Fork());
            var session = new ExamSession(settings, Config, Norm(grid), Scale);
            return (ExamSimulator.Run(session, observer), field);
        }

        public static string RepoFile(params string[] parts) => Path.Combine(Path.GetDirectoryName(ConfigDir), Path.Combine(parts));
    }
}
