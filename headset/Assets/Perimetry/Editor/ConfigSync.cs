using System.IO;
using System.Linq;
using Perimetry.Core.Config;
using Perimetry.Headset.Logic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Perimetry.Headset.Editor
{
    /// <summary>
    /// Copies the repository's config/ (the single source of every clinical constant) into
    /// Assets/Resources/PerimetryConfig with an index, on editor load, from the menu and before every build.
    /// The copy is generated (git-ignored); edit config/ at the repository root, never the copy.
    /// A build fails if the config does not load.
    /// </summary>
    [InitializeOnLoad]
    public static class ConfigSync
    {
        public const string Destination = "Assets/Resources/" + ConfigLoader.ResourceRoot;

        static ConfigSync()
        {
            EditorApplication.delayCall += () => { try { Sync(); } catch (System.Exception e) { Debug.LogError("[Perimetry] config sync failed: " + e.Message); } };
        }

        public static string SourceDirectory => Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "config"));

        [MenuItem("Perimetry/Sync Config")]
        public static void SyncMenu()
        {
            var hash = Sync();
            Debug.Log("[Perimetry] config synced, hash " + hash);
        }

        /// <summary>Returns the config hash. Throws when the config is invalid.</summary>
        public static string Sync()
        {
            var files = ClinicalConfig.ReadFiles(SourceDirectory);
            var cfg = ClinicalConfig.LoadFromFiles(files);
            HeadsetConfig.From(cfg);

            string dst = Path.GetFullPath(Path.Combine(Application.dataPath, "..", Destination));
            bool changed = false;
            foreach (var kv in files)
            {
                string target = Path.Combine(dst, kv.Key);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                if (File.Exists(target) && File.ReadAllText(target) == kv.Value) continue;
                File.WriteAllText(target, kv.Value);
                changed = true;
            }
            if (Directory.Exists(dst))
                foreach (var stale in Directory.GetFiles(dst, "*.json", SearchOption.AllDirectories))
                {
                    string rel = stale.Substring(dst.Length).TrimStart(Path.DirectorySeparatorChar, '/').Replace('\\', '/');
                    if (!files.ContainsKey(rel)) { File.Delete(stale); if (File.Exists(stale + ".meta")) File.Delete(stale + ".meta"); changed = true; }
                }
            string index = string.Join("\n", files.Keys.OrderBy(k => k, System.StringComparer.Ordinal)) + "\n";
            string indexPath = Path.Combine(dst, ConfigLoader.IndexName + ".txt");
            if (!File.Exists(indexPath) || File.ReadAllText(indexPath) != index) { File.WriteAllText(indexPath, index); changed = true; }
            if (changed) AssetDatabase.Refresh();
            return cfg.Hash;
        }

        private sealed class PreBuild : IPreprocessBuildWithReport
        {
            public int callbackOrder => 0;

            public void OnPreprocessBuild(BuildReport report)
            {
                try { Debug.Log("[Perimetry] build with config " + Sync()); }
                catch (System.Exception e) { throw new BuildFailedException("Perimetry config invalid: " + e.Message); }
            }
        }
    }
}
