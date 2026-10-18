using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Localization;
using Perimetry.Core.Luminance;

namespace Perimetry.Cli
{
    /// <summary>
    /// Machine-readable output for the doctor portal. Every clinical number the portal shows comes from here,
    /// i.e. from /core, so the portal never re-implements the psychophysics.
    ///   perimetry report exam.json [--out report.json]          exam summary + Single Field Analysis
    ///   perimetry check-calibration calibration.json             validates a photometric calibration file
    /// Output is strict JSON: NaN/Infinity are written as null, enums as names.
    /// </summary>
    public static class Report
    {
        public const string Schema = "perimetry-report/1";

        public static readonly JsonSerializerSettings Out = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            Converters = { new StringEnumConverter(), new FiniteDoubleConverter() },
        };

        private static readonly JsonSerializerSettings In = new JsonSerializerSettings { FloatParseHandling = FloatParseHandling.Double };

        /// <param name="normativePath">Phase 6: a normative database file (perimetry-normative/1) to analyse with instead of the provisional synthetic one.</param>
        public static int Run(ClinicalConfig cfg, string path, string outPath, string normativePath = null)
        {
            object doc;
            try
            {
                INormativeDatabase norm = null;
                if (normativePath != null)
                {
                    try { norm = NormativeDatabase.FromJson(File.ReadAllText(normativePath)); }
                    catch (Exception e) when (e is JsonException || e is FormatException || e is IOException)
                    { throw new FormatException("invalid normative file: " + e.Message); }
                }
                doc = Build(cfg, File.ReadAllText(path), norm);
            }
            catch (Exception e) when (e is JsonException || e is FormatException || e is KeyNotFoundException || e is ArgumentException)
            {
                doc = new { schema = Schema, error = e is FormatException && e.Message.StartsWith("invalid normative") ? e.Message : "invalid exam file: " + e.Message };
                Write(doc, outPath);
                return 3;
            }
            Write(doc, outPath);
            return 0;
        }

        private static void Write(object doc, string outPath)
        {
            string json = JsonConvert.SerializeObject(doc, Out);
            if (outPath == null) Console.Out.Write(json);
            else File.WriteAllText(outPath, json);
        }

        public static object Build(ClinicalConfig cfg, string examJson, INormativeDatabase normative = null)
        {
            var exam = JsonConvert.DeserializeObject<ExamRecord>(examJson, In) ?? throw new FormatException("empty exam");
            if (exam.Settings == null || exam.Locations == null || exam.Reliability == null) throw new FormatException("not an exam record");
            var grid = GridLibrary.Get(cfg, exam.Settings.GridId);
            if (normative != null && !string.Equals(normative.GridId, grid.Id, StringComparison.OrdinalIgnoreCase))
                throw new FormatException($"invalid normative file: grade {normative.GridId} não corresponde à grade do exame ({grid.Id})");
            var norm = normative ?? ProvisionalNormative.For(cfg, grid);
            var warnings = new List<string>();
            AnalysisResult analysis = null;
            if (exam.Aborted)
            {
                warnings.Add(Disclaimers.Research);
                warnings.Add("Exame interrompido antes do fim — sem análise");
            }
            else
            {
                analysis = FieldAnalysis.Analyze(exam, cfg, norm);
                warnings.AddRange(analysis.Warnings);
            }
            if (!string.IsNullOrEmpty(exam.ConfigHash) && exam.ConfigHash != cfg.Hash)
                warnings.Add("Exame feito com outra configuração clínica (" + Short(exam.ConfigHash) + "); analisado com " + Short(cfg.Hash));

            var rel = exam.Reliability;
            return new
            {
                schema = Schema,
                analyzedWith = new
                {
                    software = "perimetry-cli/" + ExamSession.Version, configHash = cfg.Hash,
                    // Phase 6: the normative database used for the analysis (null for an aborted exam).
                    normative = analysis == null ? null : new { id = norm.Id, status = norm.Status, source = norm.Source, populationEyes = norm.PopulationEyes, ageMin = norm.AgeMin, ageMax = norm.AgeMax },
                },
                exam = new
                {
                    exam.ExamId,
                    exam.StartedUtc,
                    exam.SchemaVersion,
                    exam.SoftwareVersion,
                    exam.ConfigHash,
                    exam.Settings,
                    exam.Calibrated,
                    exam.CalibrationRef,
                    exam.BackgroundCdm2,
                    BackgroundAsb = exam.BackgroundCdm2 * DbScale.AsbPerCdm2,
                    exam.MaxLuminanceCdm2,
                    exam.HfaOffsetDb,
                    StimulusDiameterDeg = Stimulus.DiameterDeg(cfg.Stimulus, exam.Settings.StimulusSize),
                    StimulusDurationMs = cfg.Stimulus.DurationMs,
                    MinMeasurableHfaDb = cfg.Strategies.MinStimulusDb + exam.HfaOffsetDb,
                    exam.NormativeId,
                    exam.NormativeProvisional,
                    exam.Aborted,
                    exam.Disclaimer,
                    GhtApplicable = grid.GhtApplicable,
                    // Phase 5: foveal threshold (null when not tested), HFA-equivalent dB; details in Foveal.
                    FovealThresholdHfaDb = exam.Foveal == null ? (double?)null : Math.Max(cfg.Strategies.MinStimulusDb, exam.Foveal.ThresholdDb) + exam.HfaOffsetDb,
                    Foveal = exam.Foveal == null ? null : new
                    {
                        exam.Foveal.ThresholdDb,
                        ThresholdHfaDb = Math.Max(cfg.Strategies.MinStimulusDb, exam.Foveal.ThresholdDb) + exam.HfaOffsetDb,
                        exam.Foveal.CensoredLow,
                        exam.Foveal.CensoredHigh,
                        exam.Foveal.StopReason,
                        exam.Foveal.Presentations,
                        exam.Foveal.Strategy,
                        ExpectedHfaDb = analysis?.FovealExpectedHfaDb,
                        Td = analysis?.FovealTd,
                        TdP = analysis?.FovealTdP,
                    },
                    Reliability = new
                    {
                        rel.FixationLosses,
                        rel.FalsePositives,
                        rel.FalseNegatives,
                        FixationLossRate = rel.FixationLosses.Rate,
                        FalsePositiveRate = rel.FalsePositives.Rate,
                        FalseNegativeRate = rel.FalseNegatives.Rate,
                        rel.GazeRejectedPresentations,
                        rel.GazeTracked,
                        rel.GazeMeanErrorDeg,
                        rel.GazeSamples,
                        GazeMaxErrorDeg = cfg.Reliability.Gaze.MaxErrorDeg,
                        rel.GazeTrace,
                        rel.AnticipatedResponses,
                        rel.AbortedPresentations,
                        rel.BlindSpotFound,
                        rel.BlindSpotX,
                        rel.BlindSpotY,
                        rel.BlindSpotRemaps,
                        rel.TotalPresentations,
                        rel.DurationMs,
                        rel.PausedMs,
                        EffectiveDurationMs = rel.DurationMs - rel.PausedMs,
                        rel.Pauses,
                        rel.LowReliability,
                    },
                    PresentationCount = exam.Presentations?.Count ?? 0,
                },
                analysis,
                text = analysis == null ? null : new
                {
                    ght = Strings.Ght(analysis.Ght),
                    ghtEn = Strings.Ght(analysis.Ght, Strings.EnUs),
                },
                warnings,
            };
        }

        private static string Short(string h) => h.Length > 12 ? h.Substring(0, 12) : h;

        public static int CheckCalibration(string path)
        {
            try
            {
                var cal = LuminanceCalibration.FromJson(File.ReadAllText(path));
                Console.Out.Write(JsonConvert.SerializeObject(new
                {
                    ok = true,
                    cal.DeviceSerial,
                    cal.Photometer,
                    cal.MeasuredAtUtc,
                    cal.ValidUntilUtc,
                    cal.IsMeasured,
                    eyes = cal.Eyes.ToDictionary(kv => kv.Key.ToString(), kv => new
                    {
                        samples = kv.Value.Samples.Count,
                        minCdm2 = kv.Value.Samples.First().Cdm2,
                        maxCdm2 = kv.Value.Samples.Last().Cdm2,
                    }),
                    missingEyes = Enum.GetValues(typeof(Eye)).Cast<Eye>().Where(e => !cal.Eyes.ContainsKey(e)).Select(e => e.ToString()),
                }, Out));
                return 0;
            }
            catch (Exception e)
            {
                Console.Out.Write(JsonConvert.SerializeObject(new { ok = false, error = e.Message }, Out));
                return 3;
            }
        }

        /// <summary>Writes NaN and ±Infinity as null so the output is valid JSON for any consumer.</summary>
        private sealed class FiniteDoubleConverter : JsonConverter
        {
            public override bool CanConvert(Type t) => t == typeof(double) || t == typeof(double?) || t == typeof(float);
            public override bool CanRead => false;
            public override object ReadJson(JsonReader r, Type t, object v, JsonSerializer s) => throw new NotSupportedException();

            public override void WriteJson(JsonWriter w, object value, JsonSerializer s)
            {
                double d = Convert.ToDouble(value);
                if (double.IsNaN(d) || double.IsInfinity(d)) w.WriteNull();
                else w.WriteValue(d);
            }
        }
    }
}
