using System.Collections.Generic;
using Perimetry.Core.Config;
using UnityEngine;

namespace Perimetry.Headset
{
    /// <summary>
    /// Loads config/ from Resources/PerimetryConfig, where the editor (ConfigSync) copies the repository's
    /// config directory before every build. index.txt lists the relative paths; the hash is computed over the
    /// same paths and contents as on the desktop, so an exam made on the headset carries the same config hash
    /// as the CLI for the same config.
    /// </summary>
    public static class ConfigLoader
    {
        public const string ResourceRoot = "PerimetryConfig";
        public const string IndexName = "index";

        public static ClinicalConfig Load()
        {
            var index = Resources.Load<TextAsset>(ResourceRoot + "/" + IndexName);
            if (index == null)
                throw new System.IO.FileNotFoundException("Resources/" + ResourceRoot + "/index.txt missing: run Perimetry > Sync Config in the editor");
            var files = new Dictionary<string, string>();
            foreach (var raw in index.text.Split('\n'))
            {
                string rel = raw.Trim();
                if (rel.Length == 0) continue;
                string res = ResourceRoot + "/" + (rel.EndsWith(".json") ? rel.Substring(0, rel.Length - 5) : rel);
                var ta = Resources.Load<TextAsset>(res);
                if (ta == null) throw new System.IO.FileNotFoundException("config resource missing: " + res);
                files[rel] = ta.text;
            }
            return ClinicalConfig.LoadFromFiles(files);
        }
    }
}
