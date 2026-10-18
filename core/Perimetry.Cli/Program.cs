using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Perimetry.Core.Analysis;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Localization;
using Perimetry.Core.Luminance;
using Perimetry.Core.MathUtil;
using Perimetry.Core.Simulation;
using Perimetry.Core.Strategies;

namespace Perimetry.Cli
{
    /// <summary>
    /// Phase 1 command line: simulate exams, analyse exam JSON, benchmark strategies, OPI cross-check.
    ///   perimetry simulate  [--grid 24-2] [--strategy Zest] [--eye OD] [--defect Normal] [--depth 15] [--age 50] [--seed 1] [--out exam.json]
    ///   perimetry analyze   exam.json [--out analysis.json]
    ///   perimetry benchmark [--grid 24-2] [--strategy Zest] [--defect Normal] [--runs 50]
    ///   perimetry crosscheck scenarios.json out.json
    ///   perimetry report exam.json [--out report.json]    (portal: strict JSON, see Report.cs)
    ///   perimetry check-calibration calibration.json
    /// </summary>
    public static class Program
    {
        private static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            FloatFormatHandling = FloatFormatHandling.String, // same as the headset; NaN as "NaN" keeps the file valid JSON
        };

        public static int Main(string[] args)
        {
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            if (args.Length == 0) { Console.Error.WriteLine("usage: perimetry simulate|analyze|benchmark|crosscheck|report|check-calibration ..."); return 2; }
            var opt = ParseOptions(args.Skip(1).ToArray(), out var positional);
            var cfg = ClinicalConfig.LoadFromDirectory(opt.TryGetValue("config", out var c) ? c : ClinicalConfig.FindConfigDirectory(Directory.GetCurrentDirectory()));
            switch (args[0])
            {
                case "simulate": return Simulate(cfg, opt);
                case "analyze": return Analyze(cfg, positional[0], opt);
                case "benchmark": return Benchmark(cfg, opt);
                case "crosscheck": return CrossCheck.Run(positional[0], positional[1]);
                case "report": return Report.Run(cfg, positional[0], Get(opt, "out", null));
                case "check-calibration": return Report.CheckCalibration(positional[0]);
                default: Console.Error.WriteLine("unknown command " + args[0]); return 2;
            }
        }

        private static Dictionary<string, string> ParseOptions(string[] a, out List<string> positional)
        {
            var d = new Dictionary<string, string>();
            positional = new List<string>();
            for (int i = 0; i < a.Length; i++)
            {
                if (a[i].StartsWith("--")) d[a[i].Substring(2)] = i + 1 < a.Length ? a[++i] : "true";
                else positional.Add(a[i]);
            }
            return d;
        }

        private static string Get(Dictionary<string, string> o, string k, string def) => o.TryGetValue(k, out var v) ? v : def;

        private sealed class SimRun
        {
            public ExamRecord Exam;
            public SyntheticField Field;
            public INormativeDatabase Norm;
            public DbScale Scale;
        }

        private static SimRun RunOne(ClinicalConfig cfg, Dictionary<string, string> o, ulong seed)
        {
            var settings = new ExamSettings
            {
                GridId = Get(o, "grid", "24-2"),
                Eye = (Eye)Enum.Parse(typeof(Eye), Get(o, "eye", "OD"), true),
                Strategy = (StrategyKind)Enum.Parse(typeof(StrategyKind), Get(o, "strategy", "Zest"), true),
                FixationMode = (FixationMode)Enum.Parse(typeof(FixationMode), Get(o, "fixation", "CentralHeijlKrakau"), true),
                AgeYears = double.Parse(Get(o, "age", "50")),
                Seed = seed,
                PatientRef = Get(o, "patient-ref", null),
            };
            var scale = DbScale.Nominal(cfg.Display);
            var grid = GridLibrary.Get(cfg, settings.GridId);
            var norm = ProvisionalNormative.For(cfg, grid);
            var rng = new Rng(seed * 7919 + 17);
            var defect = (DefectType)Enum.Parse(typeof(DefectType), Get(o, "defect", "Normal"), true);
            var field = SyntheticField.Create(cfg.ProvisionalNormative, grid, settings.AgeYears, defect, double.Parse(Get(o, "depth", "15")), rng);
            var obs = new SimulatedObserver(field, scale, settings.Eye, new ObserverParameters
            {
                FalsePositiveRate = double.Parse(Get(o, "fpr", "0.03")),
                FalseNegativeRate = double.Parse(Get(o, "fnr", "0.01")),
                FixationLossRate = double.Parse(Get(o, "fixloss", "0")),
            }, rng.Fork());
            var session = new ExamSession(settings, cfg, norm, scale);
            var exam = ExamSimulator.Run(session, obs);
            return new SimRun { Exam = exam, Field = field, Norm = norm, Scale = scale };
        }

        private static int Simulate(ClinicalConfig cfg, Dictionary<string, string> o)
        {
            var r = RunOne(cfg, o, ulong.Parse(Get(o, "seed", "1")));
            var a = FieldAnalysis.Analyze(r.Exam, cfg, r.Norm);
            PrintReport(r.Exam, a, r.Field);
            string outPath = Get(o, "out", null);
            if (outPath != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath)));
                File.WriteAllText(outPath, JsonConvert.SerializeObject(r.Exam, Json));
                var analysisPath = Path.ChangeExtension(outPath, null) + ".analysis.json";
                File.WriteAllText(analysisPath, JsonConvert.SerializeObject(new { analysis = a, truth = r.Field.TrueHfaDb, inDefect = r.Field.InDefect }, Json));
                Console.WriteLine($"wrote {outPath} and {analysisPath}");
            }
            return 0;
        }

        private static int Analyze(ClinicalConfig cfg, string path, Dictionary<string, string> o)
        {
            var exam = JsonConvert.DeserializeObject<ExamRecord>(File.ReadAllText(path), Json);
            var grid = GridLibrary.Get(cfg, exam.Settings.GridId);
            var a = FieldAnalysis.Analyze(exam, cfg, ProvisionalNormative.For(cfg, grid));
            PrintReport(exam, a, null);
            string outPath = Get(o, "out", null);
            if (outPath != null) File.WriteAllText(outPath, JsonConvert.SerializeObject(new { analysis = a }, Json));
            return 0;
        }

        private static int Benchmark(ClinicalConfig cfg, Dictionary<string, string> o)
        {
            int runs = int.Parse(Get(o, "runs", "50"));
            var mae = new List<double>(); var bias = new List<double>(); var dur = new List<double>(); var npres = new List<double>();
            var md = new List<double>(); var detect = new List<double>(); var falseFlag = new List<double>();
            for (int k = 0; k < runs; k++)
            {
                var r = RunOne(cfg, o, (ulong)(1000 + k));
                var a = FieldAnalysis.Analyze(r.Exam, cfg, r.Norm);
                var idx = r.Exam.Locations.Where(l => !l.IsBlindSpot).ToList();
                // Error is measured where the truth is inside the device's measurable range.
                var inRange = idx.Where(l => r.Scale.FromHfaEquivalent(r.Field.TrueHfaDb[l.Index]) >= cfg.Strategies.MinStimulusDb).ToList();
                if (inRange.Count > 0)
                {
                    mae.Add(inRange.Average(l => Math.Abs(l.ThresholdDb + r.Exam.HfaOffsetDb - r.Field.TrueHfaDb[l.Index])));
                    bias.Add(inRange.Average(l => l.ThresholdDb + r.Exam.HfaOffsetDb - r.Field.TrueHfaDb[l.Index]));
                }
                dur.Add(r.Exam.Reliability.DurationMs / 60000.0);
                npres.Add(r.Exam.Reliability.TotalPresentations);
                md.Add(a.Md);
                var def = a.Points.Where(p => !p.IsBlindSpot && r.Field.InDefect[p.Index]).ToList();
                var nondef = a.Points.Where(p => !p.IsBlindSpot && !r.Field.InDefect[p.Index]).ToList();
                if (def.Count > 0) detect.Add(def.Count(p => p.PdP <= 0.01) / (double)def.Count);
                if (nondef.Count > 0) falseFlag.Add(nondef.Count(p => p.PdP <= 0.01) / (double)nondef.Count);
            }
            string S(List<double> v) => v.Count == 0 ? "n/a" : $"mean {v.Average():0.00}  sd {Stats.Sd(v):0.00}  max {v.Max():0.00}";
            Console.WriteLine($"{Get(o, "grid", "24-2")} {Get(o, "strategy", "Zest")} {Get(o, "defect", "Normal")} runs={runs}");
            Console.WriteLine($"  MAE (dB, in range): {S(mae)}");
            Console.WriteLine($"  bias (dB):          {S(bias)}");
            Console.WriteLine($"  duration (min):     {S(dur)}");
            Console.WriteLine($"  presentations:      {S(npres)}");
            Console.WriteLine($"  MD (dB):            {S(md)}");
            Console.WriteLine($"  defect pts PD p<1%: {S(detect)}");
            Console.WriteLine($"  normal pts PD p<1%: {S(falseFlag)}");
            return 0;
        }

        private static void PrintReport(ExamRecord exam, AnalysisResult a, SyntheticField truth)
        {
            var rel = exam.Reliability;
            Console.WriteLine($"=== {exam.Settings.GridId} {exam.Settings.Strategy} {exam.Settings.Eye}  idade {exam.Settings.AgeYears}  seed {exam.Settings.Seed}");
            foreach (var w in a.Warnings) Console.WriteLine("!! " + w);
            Console.WriteLine($"{Strings.Get("fixationLosses")}: {rel.FixationLosses}   {Strings.Get("falsePositives")}: {rel.FalsePositives} ({rel.FalsePositives.Rate:P0})   {Strings.Get("falseNegatives")}: {rel.FalseNegatives} ({rel.FalseNegatives.Rate:P0})");
            Console.WriteLine($"{Strings.Get("duration")}: {TimeSpan.FromMilliseconds(rel.DurationMs):mm\\:ss}   apresentações: {rel.TotalPresentations}   offset HFA: {exam.HfaOffsetDb:0.0} dB");
            Console.WriteLine();
            if (exam.Settings.Strategy == StrategyKind.Screening)
            {
                Console.WriteLine("Rastreio (o = visto, r = visto no reteste, X = não visto):");
                PrintMap(exam.Settings.Eye, a.Points, p => p.ScreeningOutcome == "Seen" ? "  o" : p.ScreeningOutcome == "SeenOnRetest" ? "  r" : "  X");
                return;
            }
            Console.WriteLine("Limiares (dB dispositivo):");
            PrintMap(exam.Settings.Eye, a.Points, p => p.CensoredLow ? " <0" : $"{p.DeviceDb,3:0}");
            Console.WriteLine("Desvio total (TD):");
            PrintMap(exam.Settings.Eye, a.Points, p => p.IsBlindSpot ? "  ." : $"{p.Td,3:0}");
            Console.WriteLine("Desvio padrão (PD):");
            PrintMap(exam.Settings.Eye, a.Points, p => p.IsBlindSpot ? "  ." : $"{p.Pd,3:0}");
            Console.WriteLine("Probabilidade PD (. ns, 5 = p<5%, 2, 1, 0 = p<0,5%):");
            PrintMap(exam.Settings.Eye, a.Points, p => p.IsBlindSpot ? "  ." : "  " + Sym(p.PdP));
            Console.WriteLine($"MD {a.Md:0.00} dB {PStr(a.MdP)}   PSD {a.Psd:0.00} dB {PStr(a.PsdP)}   VFI {a.Vfi:0}%   GH {a.GeneralHeight:0.0}");
            Console.WriteLine($"GHT: {Strings.Ght(a.Ght)}  [setores: {string.Join(" ", a.GhtSectorDifferences.Select(d => d.ToString("0")))}]");
        }

        private static string Sym(double p) => p <= 0.005 ? "0" : p <= 0.01 ? "1" : p <= 0.02 ? "2" : p <= 0.05 ? "5" : ".";
        private static string PStr(double p) => p < 1 ? $"(p < {p * 100:0.#}%)" : "";

        private static void PrintMap(Eye eye, List<PointAnalysis> pts, Func<PointAnalysis, string> f)
        {
            var xs = pts.Select(p => eye == Eye.OD ? p.X : -p.X).Distinct().OrderBy(x => x).ToList();
            var ys = pts.Select(p => p.Y).Distinct().OrderByDescending(y => y).ToList();
            foreach (var y in ys)
            {
                var line = string.Concat(xs.Select(x =>
                {
                    var p = pts.FirstOrDefault(q => Math.Abs((eye == Eye.OD ? q.X : -q.X) - x) < 1e-9 && Math.Abs(q.Y - y) < 1e-9);
                    return p == null ? "    " : " " + f(p);
                }));
                Console.WriteLine(line);
            }
            Console.WriteLine();
        }
    }
}
