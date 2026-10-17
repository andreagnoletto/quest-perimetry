using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Perimetry.Core.Strategies;

namespace Perimetry.Cli
{
    /// <summary>
    /// Runs scripted response sequences through the C# ports of OPI's ZEST, fourTwo and FT so that
    /// tools/opi-crosscheck/crosscheck.R can compare stimulus sequences and final estimates with OPI.
    /// </summary>
    public static class CrossCheck
    {
        public sealed class Scenario
        {
            public string Id { get; set; }
            public string Algorithm { get; set; }
            public double[] Domain { get; set; }
            public double[] Prior { get; set; }
            public double Fpr { get; set; } = 0.03;
            public double Fnr { get; set; } = 0.03;
            public double LikelihoodSd { get; set; } = 1;
            public string StopType { get; set; } = "S";
            public double StopValue { get; set; } = 1.5;
            public double MinStimulus { get; set; }
            public double MaxStimulus { get; set; }
            public string StimChoice { get; set; } = "mean";
            public double Est { get; set; } = 25;
            /// <summary>Responses consumed in order; when exhausted the deterministic rule "seen iff stim &lt;= TrueThreshold" applies.</summary>
            public bool[] Responses { get; set; }
            public double TrueThreshold { get; set; } = 20;
        }

        public sealed class Output
        {
            public string Id { get; set; }
            public List<double> Stimuli { get; set; } = new List<double>();
            public List<bool> Responses { get; set; } = new List<bool>();
            public double Final { get; set; }
            public double? Stdev { get; set; }
        }

        public static int Run(string scenariosPath, string outPath)
        {
            var scenarios = JsonConvert.DeserializeObject<List<Scenario>>(File.ReadAllText(scenariosPath));
            var outputs = scenarios.Select(Execute).ToList();
            File.WriteAllText(outPath, JsonConvert.SerializeObject(outputs, Formatting.Indented));
            System.Console.WriteLine($"wrote {outputs.Count} scenario results to {outPath}");
            return 0;
        }

        public static Output Execute(Scenario s)
        {
            ILocationProcedure p;
            switch (s.Algorithm)
            {
                case "ZEST":
                    var lik = ZestProcedure.GaussianLikelihood(s.Domain, s.Fpr, s.Fnr, _ => s.LikelihoodSd);
                    p = new ZestProcedure(s.Domain, s.Prior ?? s.Domain.Select(_ => 1.0).ToArray(), lik, s.StopType, s.StopValue,
                        s.MinStimulus, s.MaxStimulus, 2, 2, 100, s.StimChoice);
                    break;
                case "fourTwo": p = new FourTwoProcedure(s.Est, s.MinStimulus, s.MaxStimulus); break;
                case "FT": p = new FullThresholdProcedure(s.Est, s.MinStimulus, s.MaxStimulus); break;
                default: throw new InvalidDataException("unknown algorithm " + s.Algorithm);
            }
            var o = new Output { Id = s.Id };
            int k = 0;
            while (!p.IsFinished && o.Stimuli.Count < 200)
            {
                double stim = p.NextStimulusDb();
                bool seen = s.Responses != null && k < s.Responses.Length ? s.Responses[k] : stim <= s.TrueThreshold;
                k++;
                p.Update(stim, seen);
                o.Stimuli.Add(stim);
                o.Responses.Add(seen);
            }
            o.Final = p.FinalDb;
            if (p is ZestProcedure z) o.Stdev = z.Stdev();
            return o;
        }
    }
}
