using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;

namespace Perimetry.Cli
{
    /// <summary>
    /// Phase 6: the clinic's own normative database, built from a cohort of healthy eyes exported by the portal.
    /// Every rule (config/normative/cohort.json) and every statistic is applied here, in /core; the portal only
    /// collects the candidates and shows what comes back. Output is strict JSON (NaN as null) on stdout.
    ///   perimetry normative stats   --cohort cohort.json [--grid 24-2]
    ///       -> perimetry-normative-build/1 summary without building (included, excluded, eyesByBand, status, warnings)
    ///   perimetry normative build   --cohort cohort.json --grid 24-2 --id measured-24-2 --out db.json [--built-utc ISO]
    ///       -> writes db.json (perimetry-normative/1) and prints the same summary plus the database metadata
    ///   perimetry normative inspect db.json
    ///       -> metadata and the per-location intercept / slope / SD table
    /// Exit codes: 0 ok, 3 invalid input (an error JSON is printed).
    /// </summary>
    public static class Normative
    {
        public const string BuildSchema = "perimetry-normative-build/1";
        public const string InspectSchema = "perimetry-normative-inspect/1";

        public static int Run(ClinicalConfig cfg, List<string> positional, Dictionary<string, string> opt)
        {
            string sub = positional.Count > 0 ? positional[0] : "";
            try
            {
                switch (sub)
                {
                    case "stats": return Stats(cfg, opt);
                    case "build": return Build(cfg, opt);
                    case "inspect": return Inspect(positional.Count > 1 ? positional[1] : throw new ArgumentException("usage: perimetry normative inspect db.json"));
                    default: throw new ArgumentException("usage: perimetry normative stats|build|inspect ...");
                }
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is ArgumentException || e is IOException || e is KeyNotFoundException)
            {
                Console.Out.Write(JsonConvert.SerializeObject(new { schema = BuildSchema, error = e.Message }, Report.Out));
                return 3;
            }
        }

        private static (CohortFile cohort, string json, TestGrid grid, CohortSelection sel) Select(ClinicalConfig cfg, Dictionary<string, string> opt)
        {
            if (!opt.TryGetValue("cohort", out var path)) throw new ArgumentException("--cohort cohort.json is required");
            string json = File.ReadAllText(path);
            var cohort = CohortFile.Parse(json);
            string gridId = opt.TryGetValue("grid", out var g) ? g : cohort.GridId;
            var grid = GridLibrary.Get(cfg, gridId);
            var sel = CohortFilter.Apply(cohort, cfg, grid);
            return (cohort, json, grid, sel);
        }

        // Top-level keys are lowerCamel like the report's; nested objects (exclusions, bands, rules) keep the /core property names.
        private static object Summary(CohortSelection sel, string cohortHash, ClinicalConfig cfg, object database) => new
        {
            schema = BuildSchema,
            gridId = sel.GridId,
            cohortHash,
            rules = cfg.NormativeCohort,
            configHash = cfg.Hash,
            candidates = sel.Candidates,
            included = sel.Included.Count,
            includedExams = sel.Included.Select(s => new { s.SubjectId, s.Eye, s.ExamId, s.SequenceForEye, s.AgeYears, HasFovea = s.FovealHfaDb != null }),
            excluded = sel.Excluded,
            exclusionsByReason = sel.Excluded.GroupBy(e => e.Reason).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => new { Reason = g.Key, Count = g.Count() }),
            subjects = sel.Subjects,
            eyesOd = sel.EyesOd,
            eyesOs = sel.EyesOs,
            eyesWithFovea = sel.EyesWithFovea,
            ageRange = new[] { sel.AgeMin, sel.AgeMax },
            eyesByBand = sel.EyesByBand,
            status = sel.Status,
            statusReason = sel.StatusReason,
            warnings = sel.Warnings,
            database,
        };

        private static int Stats(ClinicalConfig cfg, Dictionary<string, string> opt)
        {
            var (_, json, _, sel) = Select(cfg, opt);
            Console.Out.Write(JsonConvert.SerializeObject(Summary(sel, CohortFile.Hash(json), cfg, null), Report.Out));
            return 0;
        }

        private static int Build(ClinicalConfig cfg, Dictionary<string, string> opt)
        {
            var (_, json, grid, sel) = Select(cfg, opt);
            if (!opt.TryGetValue("out", out var outPath)) throw new ArgumentException("--out db.json is required");
            string id = opt.TryGetValue("id", out var i) ? i : "measured-" + grid.Id;
            DateTime? built = opt.TryGetValue("built-utc", out var b)
                ? DateTime.SpecifyKind(DateTime.Parse(b, null, System.Globalization.DateTimeStyles.AdjustToUniversal), DateTimeKind.Utc)
                : DateTime.UtcNow;
            string hash = CohortFile.Hash(json);
            var db = NormativeBuilder.BuildFromCohort(id, grid, cfg, sel, hash, built);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
            File.WriteAllText(outPath, db.ToJson());
            Console.Out.Write(JsonConvert.SerializeObject(Summary(sel, hash, cfg, Metadata(db, outPath)), Report.Out));
            return 0;
        }

        private static object Metadata(NormativeDatabase db, string path) => new
        {
            path,
            id = db.Id, gridId = db.GridId, status = db.Status, statusReason = db.StatusReason, source = db.Source,
            cohortHash = db.CohortHash, builder = db.Builder, builtUtc = db.BuiltUtc,
            populationEyes = db.PopulationEyes, minEyesForMeasured = db.MinEyesForMeasured, ageRange = new[] { db.AgeMin, db.AgeMax },
            locations = db.LocationCount,
            foveal = new { fromCohort = db.FovealFromCohort, populationEyes = db.FovealPopulationSize, hasData = !double.IsNaN(db.FovealIntercept) },
        };

        private static int Inspect(string path)
        {
            var db = NormativeDatabase.FromJson(File.ReadAllText(path));
            Console.Out.Write(JsonConvert.SerializeObject(new
            {
                schema = InspectSchema,
                metadata = Metadata(db, path),
                levels = db.Levels,
                locations = Enumerable.Range(0, db.LocationCount).Select(k => new
                {
                    index = k,
                    intercept = db.Intercept[k],
                    slopePerYear = db.Slope[k],
                    expectedAt50 = db.ExpectedHfa(k, 50),
                    tdSd = db.TdSdByLocation[k],
                    pdSd = db.PdSdByLocation[k],
                    tdLut = db.TdLut[k],
                    pdLut = db.PdLut[k],
                }),
                mdLut = db.MdLut,
                psdLut = db.PsdLut,
                foveal = new { intercept = db.FovealIntercept, slopePerYear = db.FovealSlope, tdSd = db.FovealTdSd, tdLut = db.FovealTdLut },
            }, Report.Out));
            return 0;
        }
    }
}
