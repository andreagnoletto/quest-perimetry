using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Perimetry.Core.Config
{
    /// <summary>
    /// All clinical constants, loaded from the versioned config/ directory.
    /// <see cref="Hash"/> (SHA-256 over every file) is stored in each exam record for traceability.
    /// </summary>
    public sealed class ClinicalConfig
    {
        public DisplayConfig Display { get; private set; }
        public StimulusConfig Stimulus { get; private set; }
        public TimingConfig Timing { get; private set; }
        public ReliabilityConfig Reliability { get; private set; }
        public StrategiesConfig Strategies { get; private set; }
        public GrayscaleConfig Grayscale { get; private set; }
        public AnalysisConfig Analysis { get; private set; }
        public GhtConfig Ght { get; private set; }
        public SyntheticNormativeConfig ProvisionalNormative { get; private set; }
        public IReadOnlyDictionary<string, GridConfig> Grids { get; private set; }
        public string Hash { get; private set; }

        public static ClinicalConfig LoadFromDirectory(string configDir)
        {
            if (!Directory.Exists(configDir))
                throw new DirectoryNotFoundException("Config directory not found: " + configDir);

            T Load<T>(string rel)
            {
                string path = Path.Combine(configDir, rel);
                return JsonConvert.DeserializeObject<T>(File.ReadAllText(path))
                       ?? throw new InvalidDataException("Empty config file: " + path);
            }

            var grids = new Dictionary<string, GridConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var f in Directory.GetFiles(Path.Combine(configDir, "grids"), "*.json").OrderBy(f => f, StringComparer.Ordinal))
            {
                var g = JsonConvert.DeserializeObject<GridConfig>(File.ReadAllText(f));
                grids[g.Id] = g;
            }

            return new ClinicalConfig
            {
                Display = Load<DisplayConfig>("display.json"),
                Stimulus = Load<StimulusConfig>("stimulus.json"),
                Timing = Load<TimingConfig>("timing.json"),
                Reliability = Load<ReliabilityConfig>("reliability.json"),
                Strategies = Load<StrategiesConfig>("strategies.json"),
                Grayscale = Load<GrayscaleConfig>("grayscale.json"),
                Analysis = Load<AnalysisConfig>("analysis.json"),
                Ght = Load<GhtConfig>("ght.json"),
                ProvisionalNormative = Load<SyntheticNormativeConfig>(Path.Combine("normative", "provisional-synthetic.json")),
                Grids = grids,
                Hash = ComputeHash(configDir),
            };
        }

        /// <summary>Walks up from <paramref name="startDir"/> until a directory containing config/display.json is found.</summary>
        public static string FindConfigDirectory(string startDir)
        {
            var dir = new DirectoryInfo(startDir);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, "config");
                if (File.Exists(Path.Combine(candidate, "display.json"))) return candidate;
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException("Could not find config/ above " + startDir);
        }

        public static string ComputeHash(string configDir)
        {
            using (var sha = SHA256.Create())
            {
                var files = Directory.GetFiles(configDir, "*.json", SearchOption.AllDirectories)
                    .Select(f => f.Substring(configDir.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/'))
                    .OrderBy(f => f, StringComparer.Ordinal);
                var sb = new StringBuilder();
                foreach (var rel in files)
                {
                    // Normalise line endings so the hash is identical on Windows checkouts.
                    string content = File.ReadAllText(Path.Combine(configDir, rel)).Replace("\r\n", "\n");
                    sb.Append(rel).Append('\n').Append(content).Append('\n');
                }
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
