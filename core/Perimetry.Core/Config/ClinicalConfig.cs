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
            return LoadFromFiles(ReadFiles(configDir));
        }

        /// <summary>
        /// Loads from an in-memory set of files keyed by their path relative to config/ ("display.json",
        /// "grids/24-2.json", ...). Used on the headset, where config/ is packaged as Unity resources.
        /// The hash is computed exactly as for a directory, so it identifies the same configuration.
        /// </summary>
        public static ClinicalConfig LoadFromFiles(IReadOnlyDictionary<string, string> files)
        {
            T Load<T>(string rel)
            {
                if (!files.TryGetValue(rel, out var text)) throw new FileNotFoundException("Missing config file: " + rel);
                return JsonConvert.DeserializeObject<T>(text) ?? throw new InvalidDataException("Empty config file: " + rel);
            }

            var grids = new Dictionary<string, GridConfig>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in files.Where(f => f.Key.StartsWith("grids/", StringComparison.Ordinal)).OrderBy(f => f.Key, StringComparer.Ordinal))
            {
                var g = JsonConvert.DeserializeObject<GridConfig>(kv.Value);
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
                ProvisionalNormative = Load<SyntheticNormativeConfig>("normative/provisional-synthetic.json"),
                Grids = grids,
                Hash = ComputeHash(files),
                Files = files,
            };
        }

        /// <summary>Raw file contents by relative path (e.g. for files read by other layers, such as headset.json).</summary>
        public IReadOnlyDictionary<string, string> Files { get; private set; }

        /// <summary>Reads every *.json under <paramref name="configDir"/>, keyed by relative path with '/' separators.</summary>
        public static Dictionary<string, string> ReadFiles(string configDir)
        {
            var root = Path.GetFullPath(configDir).TrimEnd(Path.DirectorySeparatorChar, '/');
            return Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)
                .ToDictionary(f => f.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/'),
                              File.ReadAllText, StringComparer.Ordinal);
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

        public static string ComputeHash(string configDir) => ComputeHash(ReadFiles(configDir));

        public static string ComputeHash(IReadOnlyDictionary<string, string> files)
        {
            using (var sha = SHA256.Create())
            {
                var sb = new StringBuilder();
                foreach (var rel in files.Keys.OrderBy(f => f, StringComparer.Ordinal))
                {
                    // Normalise line endings so the hash is identical on Windows checkouts.
                    sb.Append(rel).Append('\n').Append(files[rel].Replace("\r\n", "\n")).Append('\n');
                }
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
                return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
