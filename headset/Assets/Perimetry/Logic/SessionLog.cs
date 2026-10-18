using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Exam;
using Perimetry.Core.Geometry;

namespace Perimetry.Headset.Logic
{
    /// <summary>
    /// Everything that happened on the headset outside the exam presentations themselves: state changes,
    /// checks, training trials, pauses, presses outside the response window. Saved next to the exam records.
    /// </summary>
    public sealed class SessionLog
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string SoftwareVersion { get; set; }
        public string SessionId { get; set; }
        public DateTime StartedUtc { get; set; }
        public string ConfigHash { get; set; }
        public string DeviceModel { get; set; }
        public string DeviceSerial { get; set; }
        public double RefreshRateHz { get; set; }
        public bool Calibrated { get; set; }
        public string CalibrationRef { get; set; }
        public ExamOrder Order { get; set; }
        public string PresetId { get; set; }
        public List<SessionEvent> Events { get; set; } = new List<SessionEvent>();
        public List<TrainingTrialLog> Training { get; set; } = new List<TrainingTrialLog>();
        public List<string> ExamIds { get; set; } = new List<string>();
        public string Outcome { get; set; }
        public string Disclaimer { get; set; } = Disclaimers.Research;
    }

    public sealed class SessionEvent
    {
        /// <summary>ms since session start.</summary>
        public double TMs { get; set; }
        public string Type { get; set; }
        public string Detail { get; set; }
    }

    public sealed class TrainingTrialLog
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public Eye Eye { get; set; }
        public int Round { get; set; }
        public TrainingTrial Trial { get; set; }
        public bool Responded { get; set; }
        public double? LatencyMs { get; set; }
        public int EarlyPresses { get; set; }
        public bool Correct { get; set; }
    }
}
