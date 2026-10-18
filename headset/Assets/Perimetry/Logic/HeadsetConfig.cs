using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Config;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;
using Perimetry.Core.Strategies;

namespace Perimetry.Headset.Logic
{
    /// <summary>config/headset.json: patient flow, training, checks and exam presets.</summary>
    public sealed class HeadsetConfig
    {
        public const string FileName = "headset.json";

        public string Language { get; set; } = "pt-BR";
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public List<Eye> EyeOrder { get; set; } = new List<Eye> { Eye.OD, Eye.OS };
        public FixationTargetConfig FixationTarget { get; set; } = new FixationTargetConfig();
        public TrainingConfig Training { get; set; } = new TrainingConfig();
        public ChecksConfig Checks { get; set; } = new ChecksConfig();
        public int MenuHoldMs { get; set; } = 1500;
        public double DefaultAgeYears { get; set; } = 50;
        public List<ExamPreset> Presets { get; set; } = new List<ExamPreset>();
        public PresentationConfig Presentation { get; set; } = new PresentationConfig();

        public static HeadsetConfig From(ClinicalConfig cfg)
        {
            if (cfg.Files == null || !cfg.Files.TryGetValue(FileName, out var text))
                throw new InvalidOperationException("config/" + FileName + " is missing");
            return Parse(text);
        }

        public static HeadsetConfig Parse(string json)
        {
            // Replace, not append to, the default lists (eyeOrder would otherwise become OD, OS, OD, OS).
            var settings = new JsonSerializerSettings { ObjectCreationHandling = ObjectCreationHandling.Replace };
            var h = JsonConvert.DeserializeObject<HeadsetConfig>(json, settings) ?? throw new FormatException("empty headset config");
            h.Validate();
            return h;
        }

        public void Validate()
        {
            if (Presets.Count == 0) throw new FormatException("headset config needs at least one preset");
            if (Training.Trials.Count < 1) throw new FormatException("training needs at least one trial");
            if (EyeOrder.Count == 0) throw new FormatException("eyeOrder is empty");
            foreach (var p in Presets)
                if (p.Eyes == null || p.Eyes.Count == 0) throw new FormatException("preset " + p.Id + " has no eyes");
        }

        public ExamPreset Preset(string id) =>
            Presets.Find(p => p.Id == id) ?? throw new KeyNotFoundException("unknown preset " + id);
    }

    public sealed class FixationTargetConfig
    {
        public string Shape { get; set; } = "diamond";
        public string Color { get; set; } = "yellow";
        public double LuminanceCdm2 { get; set; } = 30;
        public double SizeDeg { get; set; } = 0.8;
    }

    public sealed class PresentationConfig
    {
        public double FallbackIpdMm { get; set; } = 63;
        public double TextLuminanceCdm2 { get; set; } = 2;
        public double MessageDistanceM { get; set; } = 1.5;
        public double MessageOffsetDeg { get; set; } = -6;
        public double BackgroundShellRadiusM { get; set; } = 10;
    }

    public sealed class TrainingTrial
    {
        public double X { get; set; }
        public double Y { get; set; }
        /// <summary>Device dB.</summary>
        public double Db { get; set; }
        public bool StimulusShown { get; set; } = true;
    }

    public sealed class TrainingConfig
    {
        public List<TrainingTrial> Trials { get; set; } = new List<TrainingTrial>();
        public int FeedbackMs { get; set; } = 2000;
        public int RepeatIfMissedMoreThan { get; set; } = 2;
        public int MaxRepeats { get; set; } = 1;
    }

    public sealed class ChecksConfig
    {
        public int OcclusionWindowMs { get; set; } = 5000;
        public double OcclusionDiscDeg { get; set; } = 10;
    }

    public sealed class ExamPreset
    {
        public string Id { get; set; }
        public string Label { get; set; }
        public string GridId { get; set; } = "24-2";
        [JsonConverter(typeof(StringEnumConverter))]
        public StrategyKind Strategy { get; set; } = StrategyKind.Zest;
        [JsonConverter(typeof(StringEnumConverter))]
        public FixationMode FixationMode { get; set; } = FixationMode.CentralHeijlKrakau;
        [JsonConverter(typeof(StringEnumConverter))]
        public GoldmannSize StimulusSize { get; set; } = GoldmannSize.III;
        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public List<Eye> Eyes { get; set; } = new List<Eye>();
    }

    /// <summary>
    /// What the doctor ordered for this patient. Read from an exam-order file on the headset when present
    /// (pushed by the portal or adb); otherwise built on the operator setup screen.
    /// </summary>
    public sealed class ExamOrder
    {
        public string PatientRef { get; set; }
        public double? AgeYears { get; set; }
        public string RefractiveCorrection { get; set; }
        public string PresetId { get; set; }
        /// <summary>Seed for the exam PRNG; recorded in the exam so it can be replayed. Null = derived from the clock.</summary>
        public ulong? Seed { get; set; }

        public static ExamOrder Parse(string json) => JsonConvert.DeserializeObject<ExamOrder>(json) ?? new ExamOrder();
    }
}
