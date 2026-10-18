using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Analysis
{
    /// <summary>One healthy subject's exam for building a normative database (HFA-equivalent dB, one value per grid location).</summary>
    public sealed class NormalSubject
    {
        public string Id { get; set; }
        public double Age { get; set; }
        public double[] Sensitivity { get; set; }
        /// <summary>Foveal sensitivity (HFA-equivalent dB), NaN when not measured.</summary>
        public double Foveal { get; set; } = double.NaN;
    }

    /// <summary>Normative database built from a population of normal fields (see <see cref="NormativeBuilder"/>).</summary>
    public sealed class NormativeDatabase : INormativeDatabase
    {
        public const string Schema = "perimetry-normative/1";
        public const string StatusMeasured = "MEASURED";
        public const string StatusProvisional = "PROVISIONAL";
        public const string SourceSynthetic = "synthetic";
        public const string SourceCohort = "cohort";

        public string Id { get; internal set; }
        public string GridId { get; internal set; }
        public bool IsProvisional { get; internal set; }
        public string Status => IsProvisional ? StatusProvisional : StatusMeasured;
        /// <summary>Why the status is what it is (e.g. "synthetic model" or "42 eyes, minimum 100").</summary>
        public string StatusReason { get; internal set; }
        public string Source { get; internal set; } = SourceSynthetic;
        /// <summary>SHA-256 of the cohort file the database was built from (cohort source only).</summary>
        public string CohortHash { get; internal set; }
        public string Builder { get; internal set; }
        public DateTime? BuiltUtc { get; internal set; }
        public int MinEyesForMeasured { get; internal set; }
        public int LocationCount => Intercept.Length;
        public int PopulationSize { get; internal set; }
        public int PopulationEyes => PopulationSize;
        public double AgeMin { get; internal set; } = double.NaN;
        public double AgeMax { get; internal set; } = double.NaN;
        /// <summary>True when the foveal regression comes from measured cohort values (false: synthetic model or no foveal data).</summary>
        public bool FovealFromCohort { get; internal set; }

        public double[] Intercept { get; internal set; }
        public double[] Slope { get; internal set; }
        public double[] TdSdByLocation { get; internal set; }
        public double[] PdSdByLocation { get; internal set; }
        /// <summary>Lower-tail probability levels, ascending (e.g. 0.005, 0.01, 0.02, 0.05).</summary>
        public double[] Levels { get; internal set; }
        /// <summary>[location][level] quantile of TD in normals.</summary>
        public double[][] TdLut { get; internal set; }
        public double[][] PdLut { get; internal set; }
        public double[] MdLut { get; internal set; }
        /// <summary>Upper-tail: PSD quantile at 1 - level.</summary>
        public double[] PsdLut { get; internal set; }
        internal double[] GhSorted { get; set; }
        internal double[][] GhtAbsDiffSorted { get; set; }
        /// <summary>Foveal age regression (NaN when the population had no foveal values).</summary>
        public double FovealIntercept { get; internal set; } = double.NaN;
        public double FovealSlope { get; internal set; } = double.NaN;
        public double FovealTdSd { get; internal set; } = double.NaN;
        public double[] FovealTdLut { get; internal set; }
        public int FovealPopulationSize { get; internal set; }

        public double ExpectedHfa(int i, double age) => Intercept[i] + Slope[i] * age;
        public double ExpectedFovealHfa(double age) => FovealIntercept + FovealSlope * age;
        public double FovealTdProbability(double td) => FovealTdLut == null || double.IsNaN(FovealTdLut[0]) ? 1.0 : Classify(td, FovealTdLut);
        public double TdSd(int i) => TdSdByLocation[i];
        public double PdSd(int i) => PdSdByLocation[i];

        private double Classify(double value, double[] lut)
        {
            for (int k = 0; k < Levels.Length; k++)
                if (value < lut[k]) return Levels[k];
            return 1.0;
        }

        public double TdProbability(int i, double td) => double.IsNaN(TdLut[i][0]) ? 1.0 : Classify(td, TdLut[i]);
        public double PdProbability(int i, double pd) => double.IsNaN(PdLut[i][0]) ? 1.0 : Classify(pd, PdLut[i]);
        public double MdProbability(double md) => Classify(md, MdLut);

        public double PsdProbability(double psd)
        {
            for (int k = 0; k < Levels.Length; k++)
                if (psd > PsdLut[k]) return Levels[k];
            return 1.0;
        }

        public double GhQuantile(double p) => Stats.QuantileSorted(GhSorted, p);
        public double GhtSectorDiffQuantile(int sector, double p) => Stats.QuantileSorted(GhtAbsDiffSorted[sector], p);

        // ------------------------------------------------------------------ serialisation (phase 6)

        private static readonly JsonSerializerSettings FileJson = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver(),
            FloatFormatHandling = FloatFormatHandling.String, // NaN (blind-spot percentiles, missing fovea) stays valid JSON
            DateFormatHandling = DateFormatHandling.IsoDateFormat,
            DateTimeZoneHandling = DateTimeZoneHandling.Utc,
            NullValueHandling = NullValueHandling.Include,
        };

        /// <summary>On-disk form of a normative database (schema perimetry-normative/1). Field names are the file's keys.</summary>
        public sealed class NormativeFile
        {
            public string Schema { get; set; } = NormativeDatabase.Schema;
            public string Id { get; set; }
            public string GridId { get; set; }
            public string Status { get; set; }
            public string StatusReason { get; set; }
            public NormativeSource Source { get; set; }
            public DateTime? BuiltUtc { get; set; }
            public int PopulationEyes { get; set; }
            public double[] AgeRange { get; set; }
            public int Locations { get; set; }
            public double[] Levels { get; set; }
            public double[] Intercept { get; set; }
            public double[] Slope { get; set; }
            public double[] TdSd { get; set; }
            public double[] PdSd { get; set; }
            public double[][] TdLut { get; set; }
            public double[][] PdLut { get; set; }
            public double[] MdLut { get; set; }
            public double[] PsdLut { get; set; }
            public double[] GhSorted { get; set; }
            public double[][] GhtAbsDiffSorted { get; set; }
            public NormativeFoveal Foveal { get; set; }
        }

        public sealed class NormativeSource
        {
            public string Kind { get; set; }
            public string CohortHash { get; set; }
            public string Builder { get; set; }
            public int MinEyesForMeasured { get; set; }
        }

        public sealed class NormativeFoveal
        {
            public bool FromCohort { get; set; }
            public int PopulationEyes { get; set; }
            public double Intercept { get; set; }
            public double Slope { get; set; }
            public double TdSd { get; set; }
            public double[] TdLut { get; set; }
        }

        public NormativeFile ToFile() => new NormativeFile
        {
            Id = Id, GridId = GridId, Status = Status, StatusReason = StatusReason,
            Source = new NormativeSource { Kind = Source, CohortHash = CohortHash, Builder = Builder, MinEyesForMeasured = MinEyesForMeasured },
            BuiltUtc = BuiltUtc, PopulationEyes = PopulationSize,
            AgeRange = new[] { AgeMin, AgeMax }, Locations = LocationCount, Levels = Levels,
            Intercept = Intercept, Slope = Slope, TdSd = TdSdByLocation, PdSd = PdSdByLocation,
            TdLut = TdLut, PdLut = PdLut, MdLut = MdLut, PsdLut = PsdLut, GhSorted = GhSorted, GhtAbsDiffSorted = GhtAbsDiffSorted,
            Foveal = new NormativeFoveal
            {
                FromCohort = FovealFromCohort, PopulationEyes = FovealPopulationSize,
                Intercept = FovealIntercept, Slope = FovealSlope, TdSd = FovealTdSd, TdLut = FovealTdLut,
            },
        };

        public string ToJson() => JsonConvert.SerializeObject(ToFile(), FileJson);

        public static NormativeDatabase FromJson(string json)
        {
            var f = JsonConvert.DeserializeObject<NormativeFile>(json, FileJson) ?? throw new FormatException("empty normative file");
            return FromFile(f);
        }

        public static NormativeDatabase FromFile(NormativeFile f)
        {
            if (f.Schema != Schema) throw new FormatException("not a normative database file (schema " + (f.Schema ?? "missing") + ")");
            if (f.Intercept == null || f.Slope == null || f.TdLut == null || f.PdLut == null || f.Levels == null || f.GhSorted == null || f.GhtAbsDiffSorted == null)
                throw new FormatException("normative file is missing arrays");
            int n = f.Intercept.Length;
            if (f.Slope.Length != n || f.TdSd.Length != n || f.PdSd.Length != n || f.TdLut.Length != n || f.PdLut.Length != n)
                throw new FormatException("normative file arrays disagree on the number of locations");
            var db = new NormativeDatabase
            {
                Id = f.Id, GridId = f.GridId, IsProvisional = f.Status != StatusMeasured, StatusReason = f.StatusReason,
                Source = f.Source?.Kind ?? SourceCohort, CohortHash = f.Source?.CohortHash, Builder = f.Source?.Builder,
                MinEyesForMeasured = f.Source?.MinEyesForMeasured ?? 0,
                BuiltUtc = f.BuiltUtc, PopulationSize = f.PopulationEyes,
                AgeMin = f.AgeRange != null && f.AgeRange.Length == 2 ? f.AgeRange[0] : double.NaN,
                AgeMax = f.AgeRange != null && f.AgeRange.Length == 2 ? f.AgeRange[1] : double.NaN,
                Levels = f.Levels, Intercept = f.Intercept, Slope = f.Slope, TdSdByLocation = f.TdSd, PdSdByLocation = f.PdSd,
                TdLut = f.TdLut, PdLut = f.PdLut, MdLut = f.MdLut, PsdLut = f.PsdLut, GhSorted = f.GhSorted, GhtAbsDiffSorted = f.GhtAbsDiffSorted,
            };
            if (f.Foveal != null)
            {
                db.FovealFromCohort = f.Foveal.FromCohort; db.FovealPopulationSize = f.Foveal.PopulationEyes;
                db.FovealIntercept = f.Foveal.Intercept; db.FovealSlope = f.Foveal.Slope; db.FovealTdSd = f.Foveal.TdSd; db.FovealTdLut = f.Foveal.TdLut;
            }
            return db;
        }
    }

    /// <summary>
    /// Builds a normative database from normal fields, following visualFields::nvgenerate
    /// (Apache-2.0): pointwise linear regression of sensitivity on age, TD = sens - age model,
    /// PD = TD - general height, empirical lower-tail percentiles of TD and PD per location, and
    /// empirical distributions of the global indices (MD, PSD, GH, GHT sector differences).
    /// Blind-spot locations get regression coefficients (needed as strategy start values) but no
    /// percentiles.
    /// </summary>
    public static class NormativeBuilder
    {
        public static NormativeDatabase Build(string id, bool provisional, TestGrid grid, IReadOnlyList<NormalSubject> population,
            AnalysisConfig analysis, GhtConfig ght)
        {
            int n = grid.Count;
            if (population.Count < 30) throw new ArgumentException("need at least 30 normal subjects");
            if (population.Any(s => s.Sensitivity.Length != n)) throw new ArgumentException("subject data does not match grid");
            var ages = population.Select(s => s.Age).ToArray();
            var analysisIdx = grid.AnalysisLocations.Select(l => l.Index).ToArray();
            var levels = analysis.ProbabilityLevels.OrderBy(x => x).ToArray();

            var db = new NormativeDatabase
            {
                Id = id, GridId = grid.Id, IsProvisional = provisional, PopulationSize = population.Count,
                Builder = "perimetry-core/" + Exam.ExamSession.Version + " nvgenerate-port",
                AgeMin = ages.Min(), AgeMax = ages.Max(),
                Intercept = new double[n], Slope = new double[n],
                TdSdByLocation = new double[n], PdSdByLocation = new double[n],
                Levels = levels, TdLut = new double[n][], PdLut = new double[n][],
            };

            for (int i = 0; i < n; i++)
            {
                var (a, b) = Stats.LinearFit(ages, population.Select(s => s.Sensitivity[i]).ToArray());
                db.Intercept[i] = a; db.Slope[i] = b;
            }

            var td = population.Select(s => Enumerable.Range(0, n).Select(i => s.Sensitivity[i] - db.ExpectedHfa(i, s.Age)).ToArray()).ToList();
            var gh = td.Select(t => GlobalIndices.GeneralHeight(t, analysisIdx, analysis.GeneralHeightPercentile)).ToArray();
            var pd = td.Select((t, k) => t.Select(v => v - gh[k]).ToArray()).ToList();

            var nan = Enumerable.Repeat(double.NaN, levels.Length).ToArray();
            for (int i = 0; i < n; i++)
            {
                if (grid.Locations[i].IsBlindSpot)
                {
                    db.TdSdByLocation[i] = db.PdSdByLocation[i] = double.NaN;
                    db.TdLut[i] = nan; db.PdLut[i] = nan;
                    continue;
                }
                var tdi = td.Select(t => t[i]).ToArray();
                var pdi = pd.Select(t => t[i]).ToArray();
                db.TdSdByLocation[i] = Stats.Sd(tdi);
                db.PdSdByLocation[i] = Stats.Sd(pdi);
                db.TdLut[i] = levels.Select(p => Stats.Quantile(tdi, p)).ToArray();
                db.PdLut[i] = levels.Select(p => Stats.Quantile(pdi, p)).ToArray();
            }

            // Foveal threshold (phase 5): same age regression and lower-tail percentiles, from the subjects that have it.
            var withFovea = population.Where(s => !double.IsNaN(s.Foveal)).ToList();
            if (withFovea.Count >= 30)
            {
                var (fa, fb) = Stats.LinearFit(withFovea.Select(s => s.Age).ToArray(), withFovea.Select(s => s.Foveal).ToArray());
                db.FovealIntercept = fa; db.FovealSlope = fb; db.FovealPopulationSize = withFovea.Count;
                var ftd = withFovea.Select(s => s.Foveal - db.ExpectedFovealHfa(s.Age)).ToArray();
                db.FovealTdSd = Stats.Sd(ftd);
                db.FovealTdLut = levels.Select(p => Stats.Quantile(ftd, p)).ToArray();
            }

            var md = td.Select(t => GlobalIndices.MeanDeviation(t, db.TdSdByLocation, analysisIdx, analysis.MdWeighting)).ToArray();
            var psd = td.Select((t, k) => GlobalIndices.PatternStandardDeviation(t, db.TdSdByLocation, analysisIdx, md[k])).ToArray();
            db.MdLut = levels.Select(p => Stats.Quantile(md, p)).ToArray();
            db.PsdLut = levels.Select(p => Stats.Quantile(psd, 1 - p)).ToArray();
            db.GhSorted = gh.OrderBy(v => v).ToArray();

            var sectors = GlobalIndices.ResolveSectors(ght, grid);
            var diffs = pd.Select(p =>
            {
                var prob = Enumerable.Range(0, n).Select(i => db.PdProbability(i, p[i])).ToArray();
                return GlobalIndices.GhtSectorDifferences(prob, sectors, ght.PointScores);
            }).ToList();
            db.GhtAbsDiffSorted = Enumerable.Range(0, sectors.Count)
                .Select(s => diffs.Select(d => Math.Abs(d[s])).OrderBy(v => v).ToArray()).ToArray();
            return db;
        }

        /// <summary>
        /// Phase 6: builds the clinic's own database from a filtered cohort (<see cref="CohortFilter.Apply"/>).
        /// Status is MEASURED with at least <see cref="NormativeCohortConfig.MinEyesForMeasured"/> eyes, else PROVISIONAL
        /// (still usable, with a warning on every report). Blind-spot locations without a measured value take the
        /// mean of the subject's two nearest analysed locations, so the regression there still yields a start value.
        /// </summary>
        public static NormativeDatabase BuildFromCohort(string id, TestGrid grid, ClinicalConfig cfg, CohortSelection selection,
            string cohortHash, DateTime? builtUtc)
        {
            var rules = cfg.NormativeCohort;
            int eyes = selection.Population.Count;
            bool provisional = eyes < rules.MinEyesForMeasured;
            var db = Build(id, provisional, grid, selection.Population, cfg.Analysis, cfg.Ght);
            db.Source = NormativeDatabase.SourceCohort;
            db.CohortHash = cohortHash;
            db.BuiltUtc = builtUtc;
            db.MinEyesForMeasured = rules.MinEyesForMeasured;
            db.StatusReason = provisional
                ? $"{eyes} olhos elegíveis, mínimo {rules.MinEyesForMeasured} para MEASURED"
                : $"{eyes} olhos elegíveis (mínimo {rules.MinEyesForMeasured})";
            db.FovealFromCohort = db.FovealPopulationSize > 0;
            if (!db.FovealFromCohort)
            {
                // Fewer than 30 foveal values in the cohort: fall back to the synthetic foveal model so the foveal
                // test still has an expected value; the file says so (foveal.fromCohort = false).
                var syn = ProvisionalNormative.For(cfg, grid);
                db.FovealIntercept = syn.FovealIntercept; db.FovealSlope = syn.FovealSlope;
                db.FovealTdSd = syn.FovealTdSd; db.FovealTdLut = syn.FovealTdLut; db.FovealPopulationSize = 0;
            }
            return db;
        }
    }

    /// <summary>
    /// PROVISIONAL synthetic normative database: a seeded synthetic normal population generated from a
    /// literature-based hill-of-vision model (config/normative/provisional-synthetic.json), fed through
    /// <see cref="NormativeBuilder"/>. Every output that uses it must carry the provisional warning.
    /// </summary>
    public static class ProvisionalNormative
    {
        /// <summary>Model mean sensitivity (HFA-equivalent dB) at an OD-convention location.</summary>
        public static double ModelMean(SyntheticNormativeConfig c, FieldPoint p, double age) =>
            c.PeakDb + c.EccentricitySlopeDbPerDeg * p.Eccentricity
            + c.SuperiorExtraSlopeDbPerDeg * Math.Max(0, p.Y)
            + c.AgeSlopeDbPerDecade / 10.0 * (age - c.ReferenceAge);

        public static double ModelLocalSd(SyntheticNormativeConfig c, FieldPoint p) =>
            c.LocalSd.InterceptDb + c.LocalSd.PerDegDb * p.Eccentricity;

        /// <summary>Model mean foveal sensitivity (HFA-equivalent dB): the hill of vision at eccentricity 0 plus <see cref="SyntheticNormativeConfig.FovealExtraDb"/>.</summary>
        public static double ModelFoveal(SyntheticNormativeConfig c, double age) =>
            ModelMean(c, new FieldPoint(0, 0), age) + c.FovealExtraDb;

        public static List<NormalSubject> GeneratePopulation(SyntheticNormativeConfig c, TestGrid grid)
        {
            var rng = new Rng(c.Population.Seed);
            // Separate stream for the foveal value so the grid values are identical to the phase 1 population.
            var fovealRng = new Rng(c.Population.Seed ^ 0x0F0F0F0F0F0F0F0FUL);
            var centre = new FieldPoint(0, 0);
            var pop = new List<NormalSubject>();
            for (int k = 0; k < c.Population.Size; k++)
            {
                double age = rng.Uniform(c.Population.MinAge, c.Population.MaxAge);
                double shift = rng.Gaussian(0, c.GlobalShiftSdDb);
                var sens = grid.Locations.Select(l => ModelMean(c, l.Point, age) + shift
                    + rng.Gaussian(0, ModelLocalSd(c, l.Point))
                    + (c.MeasurementSd == null ? 0 : rng.Gaussian(0, c.MeasurementSd.InterceptDb + c.MeasurementSd.PerDegDb * l.Point.Eccentricity))).ToArray();
                double foveal = ModelFoveal(c, age) + shift + fovealRng.Gaussian(0, ModelLocalSd(c, centre))
                    + (c.MeasurementSd == null ? 0 : fovealRng.Gaussian(0, c.MeasurementSd.InterceptDb));
                pop.Add(new NormalSubject { Id = "synthetic-" + k, Age = age, Sensitivity = sens, Foveal = foveal });
            }
            return pop;
        }

        private static readonly Dictionary<string, NormativeDatabase> Cache = new Dictionary<string, NormativeDatabase>();

        public static NormativeDatabase For(ClinicalConfig cfg, TestGrid grid)
        {
            string key = cfg.Hash + "|" + grid.Id + "|" + grid.Count;
            lock (Cache)
            {
                if (Cache.TryGetValue(key, out var db)) return db;
                db = NormativeBuilder.Build("provisional-synthetic/" + grid.Id, true, grid,
                    GeneratePopulation(cfg.ProvisionalNormative, grid), cfg.Analysis, cfg.Ght);
                db.Source = NormativeDatabase.SourceSynthetic;
                db.StatusReason = "modelo sintético da literatura, não medido neste aparelho";
                db.FovealFromCohort = false;
                Cache[key] = db;
                return db;
            }
        }
    }
}
