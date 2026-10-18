using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;

namespace Perimetry.Core.Analysis
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum GhtResult
    {
        WithinNormalLimits,
        Borderline,
        OutsideNormalLimits,
        GeneralReduction,
        BorderlineAndGeneralReduction,
        AbnormallyHigh,
        NotApplicable,
    }

    public sealed class PointAnalysis
    {
        public int Index { get; set; }
        /// <summary>OD convention; the report mirrors for OS.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsBlindSpot { get; set; }
        public double DeviceDb { get; set; }
        public double HfaEqDb { get; set; }
        public bool CensoredLow { get; set; }
        public double ExpectedHfaDb { get; set; }
        public double Td { get; set; }
        public double TdP { get; set; }
        public double Pd { get; set; }
        public double PdP { get; set; }
        public int GrayBand { get; set; }
        /// <summary>Screening exams only: Seen / SeenOnRetest / NotSeen.</summary>
        public string ScreeningOutcome { get; set; }
    }

    public sealed class AnalysisResult
    {
        public string ExamId { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public Eye Eye { get; set; }
        public string GridId { get; set; }
        public double AgeYears { get; set; }
        public List<PointAnalysis> Points { get; set; } = new List<PointAnalysis>();
        public double Md { get; set; }
        public double MdP { get; set; }
        public double Psd { get; set; }
        public double PsdP { get; set; }
        public double GeneralHeight { get; set; }
        public double Vfi { get; set; }
        public GhtResult Ght { get; set; }
        public double[] GhtSectorDifferences { get; set; }
        public string GhtStatus { get; set; }
        public bool Calibrated { get; set; }
        public bool NormativeProvisional { get; set; }
        /// <summary>Phase 6: which normative database produced the deviations and probabilities.</summary>
        public string NormativeId { get; set; }
        public string NormativeStatus { get; set; }
        public string NormativeSource { get; set; }
        public int NormativePopulationEyes { get; set; }
        public double HfaOffsetDb { get; set; }
        /// <summary>False when the grid does not cover the GHT sectors (10-2); <see cref="Ght"/> is then NotApplicable.</summary>
        public bool GhtApplicable { get; set; } = true;
        /// <summary>Foveal threshold (phase 5): tested, value in HFA-equivalent dB, age-expected value, deviation and its probability.</summary>
        public bool FovealTested { get; set; }
        public double FovealHfaDb { get; set; } = double.NaN;
        public bool FovealCensoredLow { get; set; }
        public double FovealExpectedHfaDb { get; set; } = double.NaN;
        public double FovealTd { get; set; } = double.NaN;
        public double FovealTdP { get; set; } = 1;
        public ReliabilitySummary Reliability { get; set; }
        public List<string> Warnings { get; set; } = new List<string>();
    }

    /// <summary>Single Field Analysis (HFA-style) of one exam.</summary>
    public static class FieldAnalysis
    {
        public static AnalysisResult Analyze(ExamRecord exam, ClinicalConfig cfg, INormativeDatabase norm)
        {
            if (exam.Aborted) throw new InvalidOperationException("exam " + exam.ExamId + " was aborted; it cannot be analysed as a complete field");
            var grid = GridLibrary.Get(cfg, exam.Settings.GridId);
            if (norm.GridId != grid.Id) throw new ArgumentException("normative/grid mismatch");
            double age = exam.Settings.AgeYears;
            double minDb = cfg.Strategies.MinStimulusDb;
            int n = grid.Count;
            var res = new AnalysisResult
            {
                ExamId = exam.ExamId, Eye = exam.Settings.Eye, GridId = grid.Id, AgeYears = age,
                Calibrated = exam.Calibrated, NormativeProvisional = norm.IsProvisional,
                NormativeId = norm.Id, NormativeStatus = norm.Status, NormativeSource = norm.Source, NormativePopulationEyes = norm.PopulationEyes,
                HfaOffsetDb = exam.HfaOffsetDb, Reliability = exam.Reliability, GhtStatus = cfg.Ght.Status, GhtApplicable = grid.GhtApplicable,
            };

            res.Warnings.Add(Disclaimers.Research);
            if (!exam.Calibrated) res.Warnings.Add(Disclaimers.Uncalibrated + " — escala dB baseada em curva sRGB nominal");
            if (norm.IsProvisional)
            {
                // Phase 6: a clinic cohort below the minimum is "própria" but still provisional; the synthetic model keeps the original text.
                if (norm.Source == NormativeDatabase.SourceCohort)
                {
                    int min = (norm as NormativeDatabase)?.MinEyesForMeasured ?? cfg.NormativeCohort.MinEyesForMeasured;
                    res.Warnings.Add($"NORMATIVO PRÓPRIO PROVISÓRIO — {norm.PopulationEyes} olhos (mínimo {min})");
                }
                else res.Warnings.Add(Disclaimers.ProvisionalNormative);
            }
            if (!double.IsNaN(norm.AgeMin) && !double.IsNaN(norm.AgeMax))
            {
                double margin = cfg.NormativeCohort.AgeRangeMarginYears;
                if (age < norm.AgeMin - margin || age > norm.AgeMax + margin)
                    res.Warnings.Add($"Idade fora da faixa do normativo ({norm.AgeMin:0}–{norm.AgeMax:0} anos)");
            }
            if (exam.Reliability.FixationLosses.Alert) res.Warnings.Add("Perdas de fixação acima do limite");
            if (exam.Reliability.FalsePositives.Alert) res.Warnings.Add("Falsos positivos acima do limite");
            if (exam.Reliability.FalseNegatives.Alert) res.Warnings.Add("Falsos negativos acima do limite");
            if (ExamSession.HeijlKrakauActive(exam.Settings, cfg) && !exam.Reliability.BlindSpotFound)
                res.Warnings.Add("Mancha cega não localizada — monitorização Heijl-Krakau desativada");
            if (exam.Settings.ResponseMode == ResponseMode.ForcedChoice)
                res.Warnings.Add("Escolha forçada (experimental): limiares não comparáveis ao HFA; sem falsos positivos nem Heijl-Krakau"
                                 + (exam.Reliability.GazeTracked ? "" : " — fixação não monitorada"));
            if (!grid.GhtApplicable) res.Warnings.Add("GHT não se aplica à grade " + grid.Id);

            if (exam.Foveal != null)
            {
                // Foveal threshold (phase 5): HFA-equivalent value, age-expected value and deviation; "<0 dB" analysed as 0 dB.
                res.FovealTested = true;
                res.FovealCensoredLow = exam.Foveal.CensoredLow;
                res.FovealHfaDb = Math.Max(minDb, exam.Foveal.ThresholdDb) + exam.HfaOffsetDb;
                res.FovealExpectedHfaDb = norm.ExpectedFovealHfa(age);
                if (!double.IsNaN(res.FovealExpectedHfaDb))
                {
                    res.FovealTd = res.FovealHfaDb - res.FovealExpectedHfaDb;
                    res.FovealTdP = norm.FovealTdProbability(res.FovealTd);
                }
            }

            if (exam.Settings.Strategy == Strategies.StrategyKind.Screening)
            {
                res.Ght = GhtResult.NotApplicable;
                res.Md = res.Psd = res.Vfi = res.GeneralHeight = double.NaN;
                res.MdP = res.PsdP = 1;
                res.GhtSectorDifferences = new double[0];
                res.Warnings.Add("Exame de rastreio: índices de limiar não se aplicam");
                foreach (var l in grid.Locations)
                    res.Points.Add(new PointAnalysis
                    {
                        Index = l.Index, X = l.Point.X, Y = l.Point.Y, IsBlindSpot = l.IsBlindSpot,
                        DeviceDb = double.NaN, HfaEqDb = double.NaN, Td = double.NaN, Pd = double.NaN, TdP = 1, PdP = 1,
                        ExpectedHfaDb = norm.ExpectedHfa(l.Index, age),
                        ScreeningOutcome = exam.Locations[l.Index].ScreeningOutcome,
                    });
                return res;
            }

            var dev = new double[n]; var sens = new double[n]; var normal = new double[n];
            var td = new double[n]; var tdP = new double[n]; var pd = new double[n]; var pdP = new double[n];
            for (int i = 0; i < n; i++)
            {
                var lr = exam.Locations[i];
                dev[i] = Math.Max(minDb, lr.ThresholdDb); // "<0 dB" is analysed as 0 dB, as on the HFA
                sens[i] = dev[i] + exam.HfaOffsetDb;
                normal[i] = norm.ExpectedHfa(i, age);
                td[i] = sens[i] - normal[i];
            }
            var idx = grid.AnalysisLocations.Select(l => l.Index).ToArray();
            double gh = GlobalIndices.GeneralHeight(td, idx, cfg.Analysis.GeneralHeightPercentile);
            for (int i = 0; i < n; i++)
            {
                pd[i] = td[i] - gh;
                bool bs = grid.Locations[i].IsBlindSpot;
                tdP[i] = bs ? 1 : norm.TdProbability(i, td[i]);
                pdP[i] = bs ? 1 : norm.PdProbability(i, pd[i]);
            }
            var sd = Enumerable.Range(0, n).Select(norm.TdSd).ToArray();
            res.GeneralHeight = gh;
            res.Md = GlobalIndices.MeanDeviation(td, sd, idx, cfg.Analysis.MdWeighting);
            res.MdP = norm.MdProbability(res.Md);
            res.Psd = GlobalIndices.PatternStandardDeviation(td, sd, idx, res.Md);
            res.PsdP = norm.PsdProbability(res.Psd);
            res.Vfi = GlobalIndices.Vfi(grid, idx, res.Md, sens, normal, td, tdP, pdP, cfg.Analysis.Vfi);

            if (grid.GhtApplicable)
            {
                var sectors = GlobalIndices.ResolveSectors(cfg.Ght, grid);
                res.GhtSectorDifferences = GlobalIndices.GhtSectorDifferences(pdP, sectors, cfg.Ght.PointScores);
                res.Ght = ClassifyGht(res.GhtSectorDifferences, gh, norm, cfg.Ght);
            }
            else
            {
                // 10-2: the sectors of Åsman & Heijl are 24-2 points; a few coincident 10-2 points would not be a GHT.
                res.GhtSectorDifferences = new double[0];
                res.Ght = GhtResult.NotApplicable;
            }

            for (int i = 0; i < n; i++)
            {
                var l = grid.Locations[i];
                res.Points.Add(new PointAnalysis
                {
                    Index = i, X = l.Point.X, Y = l.Point.Y, IsBlindSpot = l.IsBlindSpot,
                    DeviceDb = exam.Locations[i].ThresholdDb, HfaEqDb = sens[i], CensoredLow = exam.Locations[i].CensoredLow,
                    ExpectedHfaDb = normal[i], Td = td[i], TdP = tdP[i], Pd = pd[i], PdP = pdP[i],
                    GrayBand = Grayscale.Band(sens[i], cfg.Grayscale),
                });
            }
            return res;
        }

        /// <summary>
        /// GHT decision (Åsman &amp; Heijl 1992): outside normal limits when any sector-pair difference
        /// exceeds the normals' 99th percentile, borderline above the 97th; otherwise general reduction /
        /// abnormally high from the general height percentiles.
        /// </summary>
        public static GhtResult ClassifyGht(double[] diffs, double gh, INormativeDatabase norm, GhtConfig cfg)
        {
            bool outside = false, borderline = false;
            for (int s = 0; s < diffs.Length; s++)
            {
                double a = Math.Abs(diffs[s]);
                if (a > norm.GhtSectorDiffQuantile(s, cfg.OutsideNormalLimitsPercentile)) outside = true;
                else if (a > norm.GhtSectorDiffQuantile(s, cfg.BorderlinePercentile)) borderline = true;
            }
            bool low = gh < norm.GhQuantile(cfg.GeneralReductionPercentile);
            bool high = gh > norm.GhQuantile(cfg.AbnormallyHighPercentile);
            if (outside) return GhtResult.OutsideNormalLimits;
            if (borderline) return low ? GhtResult.BorderlineAndGeneralReduction : GhtResult.Borderline;
            if (low) return GhtResult.GeneralReduction;
            if (high) return GhtResult.AbnormallyHigh;
            return GhtResult.WithinNormalLimits;
        }
    }

    public static class Grayscale
    {
        /// <summary>Band 0 (black) .. edges.Count (white) for an HFA-equivalent dB value.</summary>
        public static int Band(double hfaDb, GrayscaleConfig cfg) => cfg.EdgesDb.Count(e => hfaDb >= e);
    }
}
