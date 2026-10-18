using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Perimetry.Headset.Logic
{
    /// <summary>File access used by <see cref="PortalSync"/>; tests substitute an in-memory implementation.</summary>
    public interface ISyncFiles
    {
        bool Exists(string path);
        string ReadText(string path);
        void WriteText(string path, string text);
        /// <summary>Names (not paths) of the files directly inside <paramref name="directory"/>; empty when it does not exist.</summary>
        IReadOnlyList<string> List(string directory);
        void Move(string from, string to);
    }

    /// <summary>System.IO implementation. Writes are atomic (temp file + rename), like <see cref="ExamStorage"/>.</summary>
    public sealed class SyncFiles : ISyncFiles
    {
        private static readonly UTF8Encoding Utf8 = new UTF8Encoding(false);

        public bool Exists(string path) => File.Exists(path);
        public string ReadText(string path) => File.ReadAllText(path, Encoding.UTF8);

        public void WriteText(string path, string text)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, Utf8.GetBytes(text));
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }

        public IReadOnlyList<string> List(string directory) =>
            Directory.Exists(directory) ? Directory.GetFiles(directory).Select(Path.GetFileName).OrderBy(n => n, System.StringComparer.Ordinal).ToList() : new List<string>();

        public void Move(string from, string to)
        {
            if (File.Exists(to)) File.Delete(to);
            File.Move(from, to);
        }
    }

    /// <summary>Where the sync files live on the headset (Application.persistentDataPath).</summary>
    public sealed class SyncPaths
    {
        public const string PortalFile = "portal.json";
        public const string TokenFile = "portal-token.json";
        public const string ExamsDirName = "exams";
        public const string SentSuffix = ".sent";

        public SyncPaths(string dataDir) { DataDir = dataDir; }

        public string DataDir { get; }
        public string Portal => Path.Combine(DataDir, PortalFile);
        public string Token => Path.Combine(DataDir, TokenFile);
        public string ExamsDir => Path.Combine(DataDir, ExamsDirName);
        public string Exam(string fileName) => Path.Combine(ExamsDir, fileName);
        public string UsedPortal(System.DateTime utc) => Path.Combine(DataDir, $"portal.used-{utc:yyyyMMdd'T'HHmmss}.json");
    }

    /// <summary>What the headset tells the portal about itself when pairing.</summary>
    public sealed class SyncIdentity
    {
        public string Serial = "unknown";
        public string Model = "unknown";
        public string Software = "";
        /// <summary>device.key (base64): lets the portal verify the HMAC of every uploaded file.</summary>
        public string HmacKeyBase64;
    }
}
