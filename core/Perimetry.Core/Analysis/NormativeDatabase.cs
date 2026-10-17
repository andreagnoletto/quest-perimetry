using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    /// <summary>Normative database built from a population of normal fields (see <see cref="NormativeBuilder"/>).</summary>
    public sealed class NormativeDatabase : INormativeDatabase
    {
        public string Id { get; internal set; }
        public string GridId { get; internal set; }
        public bool IsProvisional { get; internal set; }
        public int LocationCount => Intercept.Length;
        public int PopulationSize { get; internal set; }

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

        public double ExpectedHfa(int i, double age) => Intercept[i] + Slope[i] * age;
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

        public static List<NormalSubject> GeneratePopulation(SyntheticNormativeConfig c, TestGrid grid)
        {
            var rng = new Rng(c.Population.Seed);
            var pop = new List<NormalSubject>();
            for (int k = 0; k < c.Population.Size; k++)
            {
                double age = rng.Uniform(c.Population.MinAge, c.Population.MaxAge);
                double shift = rng.Gaussian(0, c.GlobalShiftSdDb);
                var sens = grid.Locations.Select(l => ModelMean(c, l.Point, age) + shift
                    + rng.Gaussian(0, ModelLocalSd(c, l.Point))
                    + (c.MeasurementSd == null ? 0 : rng.Gaussian(0, c.MeasurementSd.InterceptDb + c.MeasurementSd.PerDegDb * l.Point.Eccentricity))).ToArray();
                pop.Add(new NormalSubject { Id = "synthetic-" + k, Age = age, Sensitivity = sens });
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
                Cache[key] = db;
                return db;
            }
        }
    }
}
