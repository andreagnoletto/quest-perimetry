using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Tests
{
    /// <summary>Phase 6: clinic normative database from a cohort file (filter rules, status, serialisation, selection in the report).</summary>
    public class Phase6NormativeTests
    {
        private static ClinicalConfig Cfg => TestEnv.Config;
        private static TestGrid Grid => TestEnv.Grid();

        /// <summary>A cohort of synthetic normal eyes (the provisional model's population, so the fields are realistic), one exam per eye.</summary>
        private static CohortFile Cohort(int eyes, double minAge = 20, double maxAge = 80, bool withFovea = true, string gridId = "24-2")
        {
            var grid = TestEnv.Grid(gridId);
            var pop = ProvisionalNormative.GeneratePopulation(Cfg.ProvisionalNormative, grid).Where(s => s.Age >= minAge && s.Age <= maxAge).Take(eyes).ToList();
            var f = new CohortFile { GridId = grid.Id, ExportedUtc = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc) };
            for (int k = 0; k < pop.Count; k++)
                f.Subjects.Add(Subject(pop[k], "S-" + (k / 2).ToString("0000"), k % 2 == 0 ? "OD" : "OS", "E" + k, 1, withFovea, grid));
            return f;
        }

        private static CohortSubject Subject(NormalSubject s, string id, string eye, string examId, int seq, bool withFovea, TestGrid grid) => new CohortSubject
        {
            SubjectId = id, Eye = eye, ExamId = examId, SequenceForEye = seq, AgeYears = s.Age, Aborted = false, Strategy = "ZestFast",
            Reliability = new CohortReliability { FixationLossRate = 0.1, FalsePositiveRate = 0.0, FalseNegativeRate = 0.1 },
            ThresholdsHfaDb = grid.Locations.Select(l => l.IsBlindSpot ? (double?)null : s.Sensitivity[l.Index]).ToArray(),
            FovealHfaDb = withFovea ? s.Foveal : (double?)null,
        };

        private static string Json(object o) => JsonConvert.SerializeObject(o);

        [Test]
        public void Filter_ExcludesEveryRule_WithItsReason()
        {
            var c = Cohort(10);
            var grid = Grid;
            var model = ProvisionalNormative.GeneratePopulation(Cfg.ProvisionalNormative, grid)[0];
            c.Subjects[0].Aborted = true;
            c.Subjects[1].Strategy = "Screening";
            c.Subjects[2].AgeYears = 16;
            c.Subjects[3].Reliability.FixationLossRate = 0.5;
            c.Subjects[4].Reliability.FalsePositiveRate = 0.2;
            c.Subjects[5].Reliability.FalseNegativeRate = 0.5;
            c.Subjects[6].ThresholdsHfaDb = new double?[3];
            c.Subjects[7].ThresholdsHfaDb[grid.AnalysisLocations.First().Index] = null;
            // Subject 8/OD: first exam and a later one -> learning effect; plus a third -> duplicate (latest kept).
            c.Subjects[8].SubjectId = "S-LEARN"; c.Subjects[8].Eye = "OD"; c.Subjects[8].SequenceForEye = 1;
            c.Subjects.Add(Subject(model, "S-LEARN", "OD", "E-second", 2, true, grid));
            c.Subjects.Add(Subject(model, "S-LEARN", "OD", "E-third", 3, true, grid));

            var sel = CohortFilter.Apply(c, Cfg, grid);
            string ReasonOf(string examId) => sel.Excluded.Single(e => e.ExamId == examId).Reason;
            Assert.That(ReasonOf("E0"), Is.EqualTo(CohortFilter.Reasons.Aborted));
            Assert.That(ReasonOf("E1"), Is.EqualTo(CohortFilter.Reasons.Strategy));
            Assert.That(ReasonOf("E2"), Is.EqualTo(CohortFilter.Reasons.Age));
            Assert.That(ReasonOf("E3"), Is.EqualTo(CohortFilter.Reasons.FixationLosses));
            Assert.That(ReasonOf("E4"), Is.EqualTo(CohortFilter.Reasons.FalsePositives));
            Assert.That(ReasonOf("E5"), Is.EqualTo(CohortFilter.Reasons.FalseNegatives));
            Assert.That(ReasonOf("E6"), Is.EqualTo(CohortFilter.Reasons.GridMismatch));
            Assert.That(ReasonOf("E7"), Is.EqualTo(CohortFilter.Reasons.MissingData));
            Assert.That(ReasonOf("E8"), Is.EqualTo(CohortFilter.Reasons.LearningEffect));
            Assert.That(ReasonOf("E-second"), Is.EqualTo(CohortFilter.Reasons.DuplicateEye));
            Assert.That(sel.Included.Select(s => s.ExamId), Is.EquivalentTo(new[] { "E9", "E-third" }));
            Assert.That(sel.Candidates, Is.EqualTo(12));
            Assert.That(sel.Status, Is.EqualTo(NormativeDatabase.StatusProvisional));
            Assert.That(sel.Warnings.Any(w => w.Contains("pelo menos 30")));
        }

        [Test]
        public void Filter_FillsBlindSpotFromNeighbours_AndCountsBands()
        {
            var c = Cohort(60);
            var sel = CohortFilter.Apply(c, Cfg, Grid);
            Assert.That(sel.Included.Count, Is.EqualTo(60));
            Assert.That(sel.Population.All(p => p.Sensitivity.All(v => !double.IsNaN(v))), "blind-spot values filled");
            var bs = Grid.Locations.First(l => l.IsBlindSpot);
            var s0 = sel.Population[0];
            var near = Grid.Locations.Where(l => !l.IsBlindSpot).OrderBy(l => Math.Pow(l.Point.X - bs.Point.X, 2) + Math.Pow(l.Point.Y - bs.Point.Y, 2)).Take(2).ToList();
            Assert.That(s0.Sensitivity[bs.Index], Is.EqualTo(near.Average(l => s0.Sensitivity[l.Index])).Within(1e-9));
            Assert.That(sel.EyesByBand.Sum(b => b.Eyes), Is.EqualTo(60));
            Assert.That(sel.EyesOd + sel.EyesOs, Is.EqualTo(60));
            Assert.That(sel.EyesWithFovea, Is.EqualTo(60));
            Assert.That(sel.Status, Is.EqualTo(NormativeDatabase.StatusProvisional));
            Assert.That(sel.Warnings.Any(w => w.Contains("PROVISÓRIO até 100")), string.Join(" | ", sel.Warnings));
        }

        [Test]
        public void Build_StatusFollowsMinimum_AndWarningsFollowStatus()
        {
            var grid = Grid;
            var small = NormativeBuilder.BuildFromCohort("m-small", grid, Cfg, CohortFilter.Apply(Cohort(40), Cfg, grid), "h", null);
            var big = NormativeBuilder.BuildFromCohort("m-big", grid, Cfg, CohortFilter.Apply(Cohort(120), Cfg, grid), "h", null);
            Assert.That(small.Status, Is.EqualTo("PROVISIONAL"));
            Assert.That(small.IsProvisional, Is.True);
            Assert.That(small.Source, Is.EqualTo("cohort"));
            Assert.That(big.Status, Is.EqualTo("MEASURED"));
            Assert.That(big.IsProvisional, Is.False);
            Assert.That(big.PopulationEyes, Is.EqualTo(120));
            Assert.That(big.FovealFromCohort, Is.True);

            var (exam, _) = TestEnv.Simulate(31);
            var aSmall = FieldAnalysis.Analyze(exam, Cfg, small);
            var aBig = FieldAnalysis.Analyze(exam, Cfg, big);
            Assert.That(aSmall.Warnings, Has.Some.EqualTo("NORMATIVO PRÓPRIO PROVISÓRIO — 40 olhos (mínimo 100)"));
            Assert.That(aSmall.Warnings, Has.None.EqualTo(Disclaimers.ProvisionalNormative));
            Assert.That(aSmall.NormativeProvisional, Is.True);
            Assert.That(aBig.Warnings, Has.None.Contains("NORMATIVO"));
            Assert.That(aBig.NormativeProvisional, Is.False);
            Assert.That(aBig.NormativeStatus, Is.EqualTo("MEASURED"));
            Assert.That(aBig.NormativeId, Is.EqualTo("m-big"));
            Assert.That(aBig.NormativePopulationEyes, Is.EqualTo(120));
            // The synthetic model keeps the original warning.
            var aSyn = FieldAnalysis.Analyze(exam, Cfg, TestEnv.Norm());
            Assert.That(aSyn.Warnings, Has.Some.EqualTo(Disclaimers.ProvisionalNormative));
            Assert.That(aSyn.NormativeSource, Is.EqualTo("synthetic"));
        }

        [Test]
        public void Analysis_WarnsWhenAgeIsOutsideTheCohortRange()
        {
            var grid = Grid;
            var db = NormativeBuilder.BuildFromCohort("m", grid, Cfg, CohortFilter.Apply(Cohort(120, 30, 60), Cfg, grid), "h", null);
            Assert.That(db.AgeMin, Is.GreaterThanOrEqualTo(30));
            Assert.That(db.AgeMax, Is.LessThanOrEqualTo(60));
            var (young, _) = TestEnv.Simulate(32, age: 20);
            var (mid, _) = TestEnv.Simulate(33, age: 63);
            var w1 = FieldAnalysis.Analyze(young, Cfg, db).Warnings;
            var w2 = FieldAnalysis.Analyze(mid, Cfg, db).Warnings;
            Assert.That(w1.Any(w => w.StartsWith("Idade fora da faixa do normativo (")), string.Join(" | ", w1));
            Assert.That(w2.Any(w => w.StartsWith("Idade fora da faixa")), Is.False, "within the 5-year margin");
        }

        [Test]
        public void Serialisation_RoundTripsAndIsDeterministic()
        {
            var grid = Grid;
            var cohort = Cohort(120);
            string cohortJson = Json(cohort);
            var built = new DateTime(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
            var db1 = NormativeBuilder.BuildFromCohort("m", grid, Cfg, CohortFilter.Apply(CohortFile.Parse(cohortJson), Cfg, grid), CohortFile.Hash(cohortJson), built);
            var db2 = NormativeBuilder.BuildFromCohort("m", grid, Cfg, CohortFilter.Apply(CohortFile.Parse(cohortJson), Cfg, grid), CohortFile.Hash(cohortJson), built);
            string json1 = db1.ToJson(), json2 = db2.ToJson();
            Assert.That(json1, Is.EqualTo(json2), "same cohort, same bytes");
            Assert.That(JObject.Parse(json1)["schema"].ToString(), Is.EqualTo("perimetry-normative/1"));

            var back = NormativeDatabase.FromJson(json1);
            Assert.That(back.Id, Is.EqualTo("m"));
            Assert.That(back.Status, Is.EqualTo("MEASURED"));
            Assert.That(back.CohortHash, Is.EqualTo(CohortFile.Hash(cohortJson)));
            Assert.That(back.BuiltUtc, Is.EqualTo(built));
            Assert.That(back.AgeMin, Is.EqualTo(db1.AgeMin));
            Assert.That(back.ToJson(), Is.EqualTo(json1), "load → save is lossless");

            var (exam, _) = TestEnv.Simulate(34, defect: Simulation.DefectType.SuperiorArcuate);
            var a1 = Report.Settings(FieldAnalysis.Analyze(exam, Cfg, db1));
            var a2 = Report.Settings(FieldAnalysis.Analyze(exam, Cfg, back));
            Assert.That(a2, Is.EqualTo(a1), "identical analysis from the loaded database");
        }

        [Test]
        public void Foveal_FallsBackToSyntheticModel_WhenTheCohortHasNoFovealValues()
        {
            var grid = Grid;
            var db = NormativeBuilder.BuildFromCohort("m", grid, Cfg, CohortFilter.Apply(Cohort(120, withFovea: false), Cfg, grid), "h", null);
            var syn = TestEnv.Norm();
            Assert.That(db.FovealFromCohort, Is.False);
            Assert.That(db.ExpectedFovealHfa(50), Is.EqualTo(syn.ExpectedFovealHfa(50)).Within(1e-9));
            var j = JObject.Parse(db.ToJson());
            Assert.That((bool)j["foveal"]["fromCohort"], Is.False);
            var withF = NormativeBuilder.BuildFromCohort("m2", grid, Cfg, CohortFilter.Apply(Cohort(120), Cfg, grid), "h", null);
            Assert.That(withF.FovealFromCohort, Is.True);
            Assert.That(withF.ExpectedFovealHfa(50), Is.Not.EqualTo(syn.ExpectedFovealHfa(50)).Within(1e-9));
        }

        [Test]
        public void Cli_StatsBuildInspectAndReportWithNormative_EndToEnd()
        {
            var dir = Path.Combine(Path.GetTempPath(), "qp-phase6-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string cohortPath = Path.Combine(dir, "cohort.json");
                File.WriteAllText(cohortPath, Json(Cohort(120)));
                string dbPath = Path.Combine(dir, "db.json");
                string cfgArg = TestEnv.ConfigDir;

                var stats = RunCli("normative", "stats", "--cohort", cohortPath, "--config", cfgArg);
                Assert.That(stats.code, Is.EqualTo(0), stats.output);
                var sj = JObject.Parse(stats.output);
                Assert.That(sj["schema"].ToString(), Is.EqualTo("perimetry-normative-build/1"));
                Assert.That((int)sj["included"], Is.EqualTo(120));
                Assert.That(sj["status"].ToString(), Is.EqualTo("MEASURED"));
                Assert.That(sj["database"].Type, Is.EqualTo(JTokenType.Null));
                Assert.That(sj["eyesByBand"].Count(), Is.EqualTo(7));

                var build = RunCli("normative", "build", "--cohort", cohortPath, "--grid", "24-2", "--id", "measured-24-2", "--out", dbPath,
                    "--built-utc", "2026-10-18T12:00:00Z", "--config", cfgArg);
                Assert.That(build.code, Is.EqualTo(0), build.output);
                var bj = JObject.Parse(build.output);
                Assert.That(bj["database"]["id"].ToString(), Is.EqualTo("measured-24-2"));
                Assert.That(bj["database"]["status"].ToString(), Is.EqualTo("MEASURED"));
                Assert.That(File.Exists(dbPath));

                var inspect = RunCli("normative", "inspect", dbPath, "--config", cfgArg);
                Assert.That(inspect.code, Is.EqualTo(0), inspect.output);
                var ij = JObject.Parse(inspect.output);
                Assert.That(ij["locations"].Count(), Is.EqualTo(Grid.Count));

                var (exam, _) = TestEnv.Simulate(35);
                string examPath = Path.Combine(dir, "exam.json");
                File.WriteAllText(examPath, JsonConvert.SerializeObject(exam, new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String }));
                var report = RunCli("report", examPath, "--normative", dbPath, "--config", cfgArg);
                Assert.That(report.code, Is.EqualTo(0), report.output);
                var rj = JObject.Parse(report.output);
                Assert.That(rj["analyzedWith"]["normative"]["id"].ToString(), Is.EqualTo("measured-24-2"));
                Assert.That(rj["analysis"]["NormativeStatus"].ToString(), Is.EqualTo("MEASURED"));
                Assert.That(rj["warnings"].Select(w => w.ToString()), Has.None.Contains("NORMATIVO"));

                var plain = RunCli("report", examPath, "--config", cfgArg);
                Assert.That(JObject.Parse(plain.output)["analysis"]["NormativeSource"].ToString(), Is.EqualTo("synthetic"));

                // Grid mismatch: a 10-2 exam with the 24-2 database is refused with an error JSON.
                var (exam102, _) = TestEnv.Simulate(36, grid: "10-2");
                string exam102Path = Path.Combine(dir, "exam102.json");
                File.WriteAllText(exam102Path, JsonConvert.SerializeObject(exam102, new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String }));
                var bad = RunCli("report", exam102Path, "--normative", dbPath, "--config", cfgArg);
                Assert.That(bad.code, Is.EqualTo(3));
                Assert.That(JObject.Parse(bad.output)["error"].ToString(), Does.Contain("24-2").And.Contain("10-2"));
            }
            finally { Directory.Delete(dir, true); }
        }

        private static (int code, string output) RunCli(params string[] args)
        {
            var old = Console.Out;
            var sw = new StringWriter();
            Console.SetOut(sw);
            try { return (Cli.Program.Main(args), sw.ToString()); }
            finally { Console.SetOut(old); }
        }

        private static class Report
        {
            public static string Settings(AnalysisResult a) => JsonConvert.SerializeObject(a, new JsonSerializerSettings { FloatFormatHandling = FloatFormatHandling.String });
        }
    }
}
