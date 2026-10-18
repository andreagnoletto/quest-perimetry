using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Exam;

namespace Perimetry.Headset.Logic
{
    /// <summary>Where the flow writes its results. Implemented by <see cref="ExamStorage"/>; tests use an in-memory sink.</summary>
    public interface IExamSink
    {
        void SaveExam(ExamRecord exam);
        void SaveSessionLog(SessionLog log);
    }

    /// <summary>
    /// Local JSON storage on the headset: one file per eye exam plus one session log, each with a sidecar
    /// "<file>.hmac" holding HMAC-SHA256(file bytes) under a per-device random key. This detects accidental
    /// or casual modification (integrity); it is not encryption and not a legal signature. Writes are atomic
    /// (temp file + rename) so a crash never leaves a truncated exam.
    /// </summary>
    public sealed class ExamStorage : IExamSink
    {
        public const string KeyFileName = "device.key";
        private readonly byte[] _key;

        public ExamStorage(string directory, byte[] key)
        {
            Directory.CreateDirectory(directory);
            DirectoryPath = directory;
            _key = key ?? throw new ArgumentNullException(nameof(key));
            if (key.Length < 32) throw new ArgumentException("HMAC key must be at least 32 bytes");
        }

        public string DirectoryPath { get; }
        public string LastWrittenPath { get; private set; }

        public static readonly JsonSerializerSettings Json = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            FloatFormatHandling = FloatFormatHandling.String,
            Converters = { new StringEnumConverter() },
        };

        /// <summary>Reads the device key, creating a new random one on first use.</summary>
        public static byte[] LoadOrCreateKey(string keyPath)
        {
            if (File.Exists(keyPath)) return Convert.FromBase64String(File.ReadAllText(keyPath).Trim());
            var key = new byte[32];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(keyPath)));
            File.WriteAllText(keyPath, Convert.ToBase64String(key));
            return key;
        }

        public void SaveExam(ExamRecord exam) =>
            Write($"exam_{exam.StartedUtc:yyyyMMdd'T'HHmmss}_{exam.Settings.Eye}_{exam.ExamId}.json", exam);

        public void SaveSessionLog(SessionLog log) =>
            Write($"session_{log.StartedUtc:yyyyMMdd'T'HHmmss}_{log.SessionId}.json", log);

        public string Write(string fileName, object payload)
        {
            string path = Path.Combine(DirectoryPath, fileName);
            byte[] bytes = new UTF8Encoding(false).GetBytes(JsonConvert.SerializeObject(payload, Json));
            AtomicWrite(path, bytes);
            AtomicWrite(path + ".hmac", Encoding.ASCII.GetBytes(Hmac(bytes)));
            LastWrittenPath = path;
            return path;
        }

        public bool Verify(string path)
        {
            string sig = path + ".hmac";
            if (!File.Exists(path) || !File.Exists(sig)) return false;
            return FixedTimeEquals(Hmac(File.ReadAllBytes(path)), File.ReadAllText(sig).Trim());
        }

        private string Hmac(byte[] data)
        {
            using (var h = new HMACSHA256(_key))
                return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        private static void AtomicWrite(string path, byte[] bytes)
        {
            string tmp = path + ".tmp";
            File.WriteAllBytes(tmp, bytes);
            if (File.Exists(path)) File.Delete(path);
            File.Move(tmp, path);
        }
    }
}
