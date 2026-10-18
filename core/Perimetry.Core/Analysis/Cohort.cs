using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Perimetry.Core.Config;
using Perimetry.Core.Geometry;
using Perimetry.Core.MathUtil;

namespace Perimetry.Core.Analysis
{
    /// <summary>
    /// Phase 6: a cohort of candidate exams from healthy volunteers, exported by the portal
    /// (schema perimetry-cohort/1) and turned into a normative database by <see cref="CohortFilter"/> +
    /// <see cref="NormativeBuilder.BuildFromCohort"/>. Thresholds are HFA-equivalent dB, one per grid
    /// location in the grid's OD convention (ExamRecord.Locations are already OD convention for both
    /// eyes, so the portal mirrors nothing); null/NaN marks a missing value (e.g. blind-spot locations).
    /// </summary>
    public sealed class CohortFile
    {
        public const string SchemaId = "perimetry-cohort/1";
        public string Schema { get; set; } = SchemaId;
        public string GridId { get; set; }
        public DateTime? ExportedUtc { get; set; }
        public List<CohortSubject> Subjects { get; set; } = new List<CohortSubject>();

        public static CohortFile Parse(string json)
        {
            var f = JsonConvert.DeserializeObject<CohortFile>(json) ?? throw new FormatException("empty cohort file");
            if (f.Schema != SchemaId) throw new FormatException("not a cohort file (schema " + (f.Schema ?? "missing") + ")");
            if (string.IsNullOrEmpty(f.GridId)) throw new FormatException("cohort file has no gridId");
            f.Subjects ??= new List<CohortSubject>();
            return f;
        }

        /// <summary>SHA-256 of the file text, recorded in the database it produces.</summary>
        public static string Hash(string json)
        {
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(json.Replace("\r\n", "\n")))).Replace("-", "").ToLowerInvariant();
        }
    }

    public sealed class CohortSubject
    {
        /// <summary>Pseudonymous subject id (the portal's study id); never a name or patient code.</summary>
        public string SubjectId { get; set; }
        /// <summary>"OD" or "OS".</summary>
        public string Eye { get; set; }
        public string ExamId { get; set; }
        /// <summary>1 for the eye's first exam, 2 for the second, ... (chronological, per subject and eye).</summary>
        public int SequenceForEye { get; set; }
        public double AgeYears { get; set; }
        public bool Aborted { get; set; }
        public string Strategy { get; set; }
        public CohortReliability Reliability { get; set; } = new CohortReliability();
        public double?[] ThresholdsHfaDb { get; set; }
        public double? FovealHfaDb { get; set; }
    }

    public sealed class CohortReliability
    {
        public double FixationLossRate { get; set; }
        public double FalsePositiveRate { get; set; }
        public double FalseNegativeRate { get; set; }
        public double? GazeRejectedRate { get; set; }
    }

    public sealed class CohortExclusion
    {
        public string SubjectId { get; set; }
        public string ExamId { get; set; }
        public string Eye { get; set; }
        /// <summary>One of <see cref="CohortFilter.Reasons"/>.</summary>
        public string Reason { get; set; }
        public string Detail { get; set; }
    }

    public sealed class AgeBandStats
    {
        public string Band { get; set; }
        public double MinAge { get; set; }
        public double MaxAge { get; set; }
        public int Eyes { get; set; }
        public int Subjects { get; set; }
        public double MeanAge { get; set; }
        /// <summary>Mean over eyes of the mean sensitivity at the analysed locations (HFA-equivalent dB) and its SD across eyes.</summary>
        public double MeanSensitivityHfaDb { get; set; }
        public double SdSensitivityHfaDb { get; set; }
        public int EyesWithFovea { get; set; }
    }

    public sealed class CohortSelection
    {
        public string GridId { get; set; }
        public int Candidates { get; set; }
        public List<CohortSubject> Included { get; set; } = new List<CohortSubject>();
        public List<CohortExclusion> Excluded { get; set; } = new List<CohortExclusion>();
        /// <summary>The included exams as builder input (missing blind-spot values filled, see <see cref="CohortFilter"/>).</summary>
        [JsonIgnore]
        public List<NormalSubject> Population { get; set; } = new List<NormalSubject>();
        public int Subjects { get; set; }
        public int EyesOd { get; set; }
        public int EyesOs { get; set; }
        public int EyesWithFovea { get; set; }
        public double AgeMin { get; set; } = double.NaN;
        public double AgeMax { get; set; } = double.NaN;
        public List<AgeBandStats> EyesByBand { get; set; } = new List<AgeBandStats>();
        public string Status { get; set; }
        public string StatusReason { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Applies config/normative/cohort.json to a cohort file. Every rule lives here, never in the portal.</summary>
    public static class CohortFilter
    {
        public static class Reasons
        {
            public const string GridMismatch = "gridMismatch";
            public const string Aborted = "aborted";
            public const string Strategy = "strategy";
            public const string Age = "age";
            public const string FixationLosses = "fixationLosses";
            public const string FalsePositives = "falsePositives";
            public const string FalseNegatives = "falseNegatives";
            public const string MissingData = "missingData";
            public const string LearningEffect = "learningEffect";
            public const string DuplicateEye = "duplicateEye";
        }

        public static CohortSelection Apply(CohortFile cohort, ClinicalConfig cfg, TestGrid grid)
        {
            var rules = cfg.NormativeCohort;
            var sel = new CohortSelection { GridId = grid.Id, Candidates = cohort.Subjects.Count };
            if (!string.Equals(cohort.GridId, grid.Id, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"cohort grid {cohort.GridId} does not match {grid.Id}");

            // 1. Per-exam rules.
            var eligible = new List<CohortSubject>();
            foreach (var s in cohort.Subjects)
            {
                string reason = null, detail = null;
                if (s.ThresholdsHfaDb == null || s.ThresholdsHfaDb.Length != grid.Count)
                { reason = Reasons.GridMismatch; detail = $"{s.ThresholdsHfaDb?.Length ?? 0} valores, grade {grid.Id} tem {grid.Count}"; }
                else if (rules.ExcludeAborted && s.Aborted) reason = Reasons.Aborted;
                else if (rules.AllowedStrategies != null && rules.AllowedStrategies.Count > 0
                         && !rules.AllowedStrategies.Any(a => string.Equals(a, s.Strategy, StringComparison.OrdinalIgnoreCase)))
                { reason = Reasons.Strategy; detail = s.Strategy; }
                else if (s.AgeYears < rules.MinAgeYears || (rules.MaxAgeYears != null && s.AgeYears > rules.MaxAgeYears.Value))
                { reason = Reasons.Age; detail = $"{s.AgeYears:0.#} anos"; }
                else if (rules.Reliability.UseAlertLimits && s.Reliability != null && s.Reliability.FixationLossRate > cfg.Reliability.FixationLoss.AlertLimit)
                { reason = Reasons.FixationLosses; detail = $"{s.Reliability.FixationLossRate:P0} > {cfg.Reliability.FixationLoss.AlertLimit:P0}"; }
                else if (rules.Reliability.UseAlertLimits && s.Reliability != null && s.Reliability.FalsePositiveRate > cfg.Reliability.FalsePositive.AlertLimit)
                { reason = Reasons.FalsePositives; detail = $"{s.Reliability.FalsePositiveRate:P0} > {cfg.Reliability.FalsePositive.AlertLimit:P0}"; }
                else if (rules.Reliability.UseAlertLimits && s.Reliability != null && s.Reliability.FalseNegativeRate > cfg.Reliability.FalseNegative.AlertLimit)
                { reason = Reasons.FalseNegatives; detail = $"{s.Reliability.FalseNegativeRate:P0} > {cfg.Reliability.FalseNegative.AlertLimit:P0}"; }
                else if (grid.AnalysisLocations.Any(l => !IsValue(s.ThresholdsHfaDb[l.Index])))
                { reason = Reasons.MissingData; detail = "limiar ausente em ponto analisado"; }

                if (reason != null) sel.Excluded.Add(new CohortExclusion { SubjectId = s.SubjectId, ExamId = s.ExamId, Eye = s.Eye, Reason = reason, Detail = detail });
                else eligible.Add(s);
            }

            // 2. Per-eye rules: learning effect (first exam) and one exam per eye.
            foreach (var g in eligible.GroupBy(s => (s.SubjectId ?? "", (s.Eye ?? "").ToUpperInvariant())))
            {
                var exams = g.OrderBy(s => s.SequenceForEye).ThenBy(s => s.ExamId, StringComparer.Ordinal).ToList();
                if (rules.ExcludeFirstExamPerEye && exams.Count > 1 && exams[0].SequenceForEye <= 1)
                {
                    var first = exams[0];
                    sel.Excluded.Add(new CohortExclusion { SubjectId = first.SubjectId, ExamId = first.ExamId, Eye = first.Eye, Reason = Reasons.LearningEffect, Detail = "primeiro exame do olho; há exame posterior elegível" });
                    exams.RemoveAt(0);
                }
                if (string.Equals(rules.OneExamPerEye, "latest", StringComparison.OrdinalIgnoreCase) && exams.Count > 1)
                {
                    foreach (var dup in exams.Take(exams.Count - 1))
                        sel.Excluded.Add(new CohortExclusion { SubjectId = dup.SubjectId, ExamId = dup.ExamId, Eye = dup.Eye, Reason = Reasons.DuplicateEye, Detail = "mantido o exame mais recente do olho" });
                    exams = new List<CohortSubject> { exams[exams.Count - 1] };
                }
                sel.Included.AddRange(exams);
            }
            sel.Included = sel.Included.OrderBy(s => s.SubjectId, StringComparer.Ordinal).ThenBy(s => s.Eye, StringComparer.Ordinal).ThenBy(s => s.ExamId, StringComparer.Ordinal).ToList();
            sel.Excluded = sel.Excluded.OrderBy(e => e.SubjectId, StringComparer.Ordinal).ThenBy(e => e.Eye, StringComparer.Ordinal).ThenBy(e => e.ExamId, StringComparer.Ordinal).ToList();

            // 3. Builder population and statistics.
            foreach (var s in sel.Included)
                sel.Population.Add(new NormalSubject
                {
                    Id = s.SubjectId + "/" + s.Eye + "/" + s.ExamId, Age = s.AgeYears,
                    Sensitivity = FillMissing(s.ThresholdsHfaDb, grid),
                    Foveal = s.FovealHfaDb ?? double.NaN,
                });
            sel.Subjects = sel.Included.Select(s => s.SubjectId).Distinct().Count();
            sel.EyesOd = sel.Included.Count(s => string.Equals(s.Eye, "OD", StringComparison.OrdinalIgnoreCase));
            sel.EyesOs = sel.Included.Count(s => string.Equals(s.Eye, "OS", StringComparison.OrdinalIgnoreCase));
            sel.EyesWithFovea = sel.Included.Count(s => s.FovealHfaDb != null && !double.IsNaN(s.FovealHfaDb.Value));
            if (sel.Included.Count > 0) { sel.AgeMin = sel.Included.Min(s => s.AgeYears); sel.AgeMax = sel.Included.Max(s => s.AgeYears); }
            var analysisIdx = grid.AnalysisLocations.Select(l => l.Index).ToArray();
            foreach (var band in rules.AgeBands ?? new List<double[]>())
            {
                if (band == null || band.Length != 2) continue;
                var inBand = sel.Included.Where(s => s.AgeYears >= band[0] && s.AgeYears < band[1] + 1).ToList();
                var means = inBand.Select(s => analysisIdx.Select(i => s.ThresholdsHfaDb[i].Value).Average()).ToList();
                sel.EyesByBand.Add(new AgeBandStats
                {
                    Band = $"{band[0]:0}–{band[1]:0}", MinAge = band[0], MaxAge = band[1],
                    Eyes = inBand.Count, Subjects = inBand.Select(s => s.SubjectId).Distinct().Count(),
                    MeanAge = inBand.Count == 0 ? double.NaN : inBand.Average(s => s.AgeYears),
                    MeanSensitivityHfaDb = means.Count == 0 ? double.NaN : means.Average(),
                    SdSensitivityHfaDb = Stats.Sd(means),
                    EyesWithFovea = inBand.Count(s => s.FovealHfaDb != null && !double.IsNaN(s.FovealHfaDb.Value)),
                });
            }
            int n = sel.Included.Count;
            sel.Status = n >= rules.MinEyesForMeasured ? NormativeDatabase.StatusMeasured : NormativeDatabase.StatusProvisional;
            sel.StatusReason = n >= rules.MinEyesForMeasured
                ? $"{n} olhos elegíveis (mínimo {rules.MinEyesForMeasured})"
                : $"{n} olhos elegíveis, mínimo {rules.MinEyesForMeasured} para MEASURED";
            if (n < 30) sel.Warnings.Add($"Coorte com {n} olhos elegíveis: são necessários pelo menos 30 para construir o normativo");
            else if (n < rules.MinEyesForMeasured) sel.Warnings.Add($"Normativo próprio continuará PROVISÓRIO até {rules.MinEyesForMeasured} olhos elegíveis (há {n})");
            foreach (var b in sel.EyesByBand.Where(b => b.Eyes > 0 && b.Eyes < 10))
                sel.Warnings.Add($"Faixa etária {b.Band} com apenas {b.Eyes} olhos");
            if (n > 0 && sel.EyesWithFovea < 30)
                sel.Warnings.Add($"Fóvea medida em {sel.EyesWithFovea} olhos (mínimo 30): o normativo foveal usará o modelo sintético");
            return sel;
        }

        private static bool IsValue(double? v) => v != null && !double.IsNaN(v.Value) && !double.IsInfinity(v.Value);

        /// <summary>Missing values (blind-spot locations the portal leaves null) take the mean of the subject's two nearest measured locations.</summary>
        internal static double[] FillMissing(double?[] values, TestGrid grid)
        {
            var filled = new double[grid.Count];
            for (int i = 0; i < grid.Count; i++)
            {
                if (IsValue(values[i])) { filled[i] = values[i].Value; continue; }
                var p = grid.Locations[i].Point;
                var nearest = grid.Locations.Where(l => l.Index != i && IsValue(values[l.Index]))
                    .OrderBy(l => Math.Pow(l.Point.X - p.X, 2) + Math.Pow(l.Point.Y - p.Y, 2)).Take(2).ToList();
                filled[i] = nearest.Count == 0 ? double.NaN : nearest.Average(l => values[l.Index].Value);
            }
            return filled;
        }
    }
}
