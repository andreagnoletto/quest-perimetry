using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Perimetry.Core.Geometry;
using Perimetry.Core.Strategies;

namespace Perimetry.Core.Exam
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PresentationKind
    {
        Threshold,
        /// <summary>No stimulus is shown; a response is a false positive.</summary>
        FalsePositiveCatch,
        /// <summary>Supraliminal stimulus (threshold - 9 dB) at a finished location; no response is a false negative.</summary>
        FalseNegativeCatch,
        /// <summary>Heijl-Krakau: bright stimulus on the mapped blind spot; a response is a fixation loss.</summary>
        BlindSpotCatch,
        /// <summary>Initial blind spot localisation.</summary>
        BlindSpotMapping,
        /// <summary>Foveal threshold (phase 5): stimulus at the centre of the diamond fixation target, before the main test.</summary>
        Foveal,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum FixationMode
    {
        /// <summary>Mode A: fixed central target, Heijl-Krakau blind spot monitoring (HFA default).</summary>
        CentralHeijlKrakau,
        /// <summary>Mode A with native eye tracking (presentations with gaze off target are discarded and repeated).</summary>
        CentralEyeTracking,
        /// <summary>Mode A with no fixation monitoring.</summary>
        CentralUnmonitored,
        /// <summary>Mode B (phase 5): the target jumps to a new position before each presentation; stimuli are placed relative to it.</summary>
        Oculokinetic,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum FixationTargetKind
    {
        /// <summary>Single central point/cross.</summary>
        Central,
        /// <summary>Four small points around an empty centre (HFA foveal test); the patient looks at the centre.</summary>
        Diamond4,
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum ResponseMode
    {
        /// <summary>Press when seen (HFA).</summary>
        YesNo,
        /// <summary>Experimental (phase 5): N marked intervals, the patient chooses the one that contained the stimulus.</summary>
        ForcedChoice,
    }

    /// <summary>What the presentation layer (headset or simulator) must show next.</summary>
    public sealed class PresentationRequest
    {
        public int Sequence { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public PresentationKind Kind { get; set; }
        public int LocationIndex { get; set; } = -1;
        /// <summary>Position relative to the fixation target, in the TESTED eye's field coordinates (degrees).</summary>
        public double FieldX { get; set; }
        public double FieldY { get; set; }
        public bool StimulusShown { get; set; }
        /// <summary>Device dB (NaN when no stimulus is shown).</summary>
        public double Db { get; set; }
        public double IncrementCdm2 { get; set; }
        public double LuminanceCdm2 { get; set; }
        [JsonConverter(typeof(StringEnumConverter))]
        public GoldmannSize Size { get; set; }
        public double SizeDeg { get; set; }
        public int DurationMs { get; set; }
        public int ResponseWindowMs { get; set; }
        /// <summary>Random inter-stimulus delay to wait before onset.</summary>
        public int PreStimulusDelayMs { get; set; }
        /// <summary>Fixation target to show for this presentation (Diamond4 for the foveal threshold).</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public FixationTargetKind FixationTarget { get; set; } = FixationTargetKind.Central;
        /// <summary>
        /// Position of the fixation target from straight ahead (degrees, tested eye's view). (0, 0) in mode A.
        /// In oculokinetic mode the presenter places the stimulus at AngularGeometry.Compose(target, (FieldX, FieldY)).
        /// </summary>
        public double TargetX { get; set; }
        public double TargetY { get; set; }
        /// <summary>Forced choice: number of response intervals (0 = yes/no presentation).</summary>
        public int Intervals { get; set; }
        /// <summary>Forced choice: 1-based interval that contains the stimulus (0 when no stimulus is shown).</summary>
        public int StimulusInterval { get; set; }
        public int IntervalMs { get; set; }
        public int IntervalGapMs { get; set; }
        /// <summary>Forced choice: duration of the target blink marking each interval.</summary>
        public int MarkerMs { get; set; }
    }

    /// <summary>What the presentation layer reports back.</summary>
    public sealed class PresentationResult
    {
        public int Sequence { get; set; }
        public bool Responded { get; set; }
        /// <summary>Response latency from stimulus onset (ms); null when no response.</summary>
        public double? LatencyMs { get; set; }
        /// <summary>False when the presenter itself rejected the presentation for fixation (disposition "fixationLost").</summary>
        public bool FixationOk { get; set; } = true;
        /// <summary>True when an eye tracker measured gaze for this presentation (phase 5).</summary>
        public bool GazeTracked { get; set; }
        /// <summary>Gaze distance from the fixation target at stimulus onset (degrees); compared with reliability.gaze.maxErrorDeg.</summary>
        public double? GazeErrorDeg { get; set; }
        /// <summary>Largest gaze distance from the target during the stimulus (degrees); used when the onset value is missing.</summary>
        public double? GazeMaxErrorDeg { get; set; }
        /// <summary>Forced choice: 1-based interval the patient chose; null when no choice was made in time.</summary>
        public int? RespondedInterval { get; set; }
        /// <summary>Stimulus onset, ms since exam start (monotonic clock of the presenter).</summary>
        public double OnsetMs { get; set; }
        /// <summary>
        /// True when the presenter aborted this presentation (patient paused, headset removed, tracking lost).
        /// Logged with disposition "aborted" and ignored by the strategy and the catch-trial counts.
        /// </summary>
        public bool Discarded { get; set; }
    }

    /// <summary>One row of the audit log: request + result + how it was interpreted.</summary>
    public sealed class PresentationLogEntry
    {
        public PresentationRequest Request { get; set; }
        public PresentationResult Result { get; set; }
        /// <summary>
        /// "used", "fixationLost" (presenter rejected), "gazeRejected" (eye tracker: gaze farther than the configured limit),
        /// "anticipated" (latency below minimum), "catch", "mapping", "remapping", "foveal", "aborted".
        /// </summary>
        public string Disposition { get; set; }
    }

    /// <summary>One gaze sample per threshold presentation, for the report's gaze trace (HFA-style bar).</summary>
    public sealed class GazeSample
    {
        public int Sequence { get; set; }
        public double OnsetMs { get; set; }
        public double ErrorDeg { get; set; }
        public bool Rejected { get; set; }
    }

    /// <summary>Result of the optional foveal threshold (device dB).</summary>
    public sealed class FovealResult
    {
        public double ThresholdDb { get; set; }
        public bool CensoredLow { get; set; }
        public bool CensoredHigh { get; set; }
        public string StopReason { get; set; }
        public int Presentations { get; set; }
        public double StartValueDb { get; set; }
        public string Strategy { get; set; }
    }

    public sealed class ExamSettings
    {
        public string GridId { get; set; } = "24-2";
        [JsonConverter(typeof(StringEnumConverter))]
        public Eye Eye { get; set; } = Eye.OD;
        [JsonConverter(typeof(StringEnumConverter))]
        public StrategyKind Strategy { get; set; } = StrategyKind.Zest;
        [JsonConverter(typeof(StringEnumConverter))]
        public FixationMode FixationMode { get; set; } = FixationMode.CentralHeijlKrakau;
        [JsonConverter(typeof(StringEnumConverter))]
        public GoldmannSize StimulusSize { get; set; } = GoldmannSize.III;
        public double AgeYears { get; set; } = 50;
        public ulong Seed { get; set; } = 1;
        public bool FalsePositiveCatchTrials { get; set; } = true;
        public bool FalseNegativeCatchTrials { get; set; } = true;
        /// <summary>Phase 5: measure the foveal threshold before the main test (HFA "Fovea: ON"). Default off, proposed.</summary>
        public bool FovealThreshold { get; set; }
        /// <summary>Phase 5: YesNo (HFA) or the experimental ForcedChoice.</summary>
        [JsonConverter(typeof(StringEnumConverter))]
        public ResponseMode ResponseMode { get; set; } = ResponseMode.YesNo;
        public string PatientRef { get; set; }
        public string RefractiveCorrection { get; set; }
    }

    public sealed class LocationResult
    {
        public int Index { get; set; }
        /// <summary>OD convention.</summary>
        public double X { get; set; }
        public double Y { get; set; }
        public bool IsBlindSpot { get; set; }
        /// <summary>Device dB (NaN for screening).</summary>
        public double ThresholdDb { get; set; }
        public bool CensoredLow { get; set; }
        public bool CensoredHigh { get; set; }
        public string StopReason { get; set; }
        public int Presentations { get; set; }
        public double StartValueDb { get; set; }
        public string ScreeningOutcome { get; set; }
    }

    public sealed class CatchCount
    {
        public int Trials { get; set; }
        public int Errors { get; set; }
        public double Rate => Trials == 0 ? double.NaN : (double)Errors / Trials;
        public double AlertLimit { get; set; }
        public bool Alert => Trials > 0 && Rate > AlertLimit;
        public override string ToString() => $"{Errors}/{Trials}";
    }

    public sealed class ReliabilitySummary
    {
        public CatchCount FixationLosses { get; set; } = new CatchCount();
        public CatchCount FalsePositives { get; set; } = new CatchCount();
        public CatchCount FalseNegatives { get; set; } = new CatchCount();
        /// <summary>Threshold presentations not used because fixation was off target (presenter-rejected or gaze beyond the limit).</summary>
        public int GazeRejectedPresentations { get; set; }
        /// <summary>True when an eye tracker measured gaze during the exam (phase 5).</summary>
        public bool GazeTracked { get; set; }
        /// <summary>Mean gaze distance from the target at stimulus onset over tracked threshold presentations (degrees; NaN when not tracked).</summary>
        public double GazeMeanErrorDeg { get; set; } = double.NaN;
        public int GazeSamples { get; set; }
        /// <summary>One sample per tracked threshold presentation (reliability.gaze.recordTrace); drawn as the gaze trace in the report.</summary>
        public List<GazeSample> GazeTrace { get; set; } = new List<GazeSample>();
        public int AnticipatedResponses { get; set; }
        public bool BlindSpotFound { get; set; }
        public double? BlindSpotX { get; set; }
        public double? BlindSpotY { get; set; }
        /// <summary>Times the blind spot was searched again after consecutive fixation losses.</summary>
        public int BlindSpotRemaps { get; set; }
        public int TotalPresentations { get; set; }
        public double DurationMs { get; set; }
        public int AbortedPresentations { get; set; }
        /// <summary>Pauses requested by the patient or operator (ms since exam start).</summary>
        public List<PauseInterval> Pauses { get; set; } = new List<PauseInterval>();
        /// <summary>Total paused time; <see cref="DurationMs"/> minus this is the effective test time.</summary>
        public double PausedMs => Pauses.Sum(p => Math.Max(0, p.EndMs - p.StartMs));
        public bool LowReliability => FixationLosses.Alert || FalsePositives.Alert || FalseNegatives.Alert;
    }

    public sealed class PauseInterval
    {
        public double StartMs { get; set; }
        public double EndMs { get; set; }
        /// <summary>"patient" (button held), "operator", "headsetRemoved", "trackingLost".</summary>
        public string Reason { get; set; }
    }

    /// <summary>Complete, self-contained record of one eye's exam (stored on the headset, synced to the portal).</summary>
    public sealed class ExamRecord
    {
        public string SchemaVersion { get; set; } = "1.0";
        public string SoftwareVersion { get; set; }
        public string ConfigHash { get; set; }
        public string ExamId { get; set; }
        public DateTime StartedUtc { get; set; }
        public ExamSettings Settings { get; set; }
        public bool Calibrated { get; set; }
        public string CalibrationRef { get; set; }
        public double BackgroundCdm2 { get; set; }
        public double MaxLuminanceCdm2 { get; set; }
        public double HfaOffsetDb { get; set; }
        public string NormativeId { get; set; }
        public bool NormativeProvisional { get; set; }
        public List<LocationResult> Locations { get; set; } = new List<LocationResult>();
        /// <summary>Foveal threshold when <see cref="ExamSettings.FovealThreshold"/> was on; null otherwise.</summary>
        public FovealResult Foveal { get; set; }
        public ReliabilitySummary Reliability { get; set; } = new ReliabilitySummary();
        public List<PresentationLogEntry> Presentations { get; set; } = new List<PresentationLogEntry>();
        /// <summary>True when the exam was stopped before every location finished; such a record must not be analysed as complete.</summary>
        public bool Aborted { get; set; }
        public string Disclaimer { get; set; } = Disclaimers.Research;
    }

    public static class Disclaimers
    {
        public const string Research = "Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA";
        public const string Uncalibrated = "NÃO CALIBRADO";
        public const string ProvisionalNormative = "NORMATIVO PROVISÓRIO — probabilidades e índices sem validade clínica";
    }
}
