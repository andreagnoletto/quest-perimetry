using System.Collections.Generic;
using Newtonsoft.Json;

namespace Perimetry.Core.Config
{
    // Plain data classes mirroring config/*.json. Property names match the JSON keys
    // (Newtonsoft matches case-insensitively). No clinical constant lives in code.

    public sealed class DisplayConfig
    {
        public double BackgroundCdm2 { get; set; }
        public double NominalMaxLuminanceCdm2 { get; set; }
        public double HfaMaxStimulusAsb { get; set; }
        public double HfaBackgroundAsb { get; set; }
        public double VirtualStimulusDistanceM { get; set; }
        public double UsableHalfFieldDeg { get; set; }
        public string NominalGamma { get; set; }
        /// <summary>Panel/swapchain quantisation; the renderer dithers the float drive level to this depth.</summary>
        public int PanelBitsPerChannel { get; set; } = 8;
        /// <summary>"none", "bayer4x4" (spatial) or "bayer4x4Temporal" (spatial pattern shifted every frame).</summary>
        public string Dithering { get; set; } = "bayer4x4Temporal";
        /// <summary>Display refresh rate requested on the headset; stimulus duration is quantised to frames.</summary>
        public double RefreshRateHz { get; set; } = 90;
        /// <summary>Luminance shown to the eye that is not being tested (0 = black, occluded).</summary>
        public double UntestedEyeLuminanceCdm2 { get; set; }
    }

    public sealed class StimulusConfig
    {
        public double GoldmannReferenceDistanceMm { get; set; }
        public Dictionary<string, double> GoldmannAreasMm2 { get; set; }
        public string DefaultSize { get; set; }
        public int DurationMs { get; set; }
        public string Color { get; set; }
    }

    public sealed class TimingConfig
    {
        public int ResponseWindowMs { get; set; }
        public int IsiMinMs { get; set; }
        public int IsiMaxMs { get; set; }
        public int MinValidLatencyMs { get; set; }
        /// <summary>Holding the response button this long pauses the exam (HFA: patient holds the button).</summary>
        public int PauseHoldMs { get; set; } = 1500;
        /// <summary>Delay after the patient resumes before the next stimulus.</summary>
        public int ResumeAfterPauseDelayMs { get; set; } = 1500;
    }

    public sealed class CatchTrialConfig
    {
        public int EveryN { get; set; }
        public int Jitter { get; set; }
        public double AlertLimit { get; set; }
        public double BrighterByDb { get; set; }
    }

    public sealed class BlindSpotConfig
    {
        public double NominalX { get; set; }
        public double NominalY { get; set; }
        public List<double[]> SearchOffsetsDeg { get; set; }
        public int PresentationsPerCandidate { get; set; }
        public double StimulusDb { get; set; }
        /// <summary>Re-run the blind-spot search after this many consecutive fixation losses (0 = never). Product owner, 2026-10-18.</summary>
        public int RemapAfterConsecutiveLosses { get; set; }
    }

    /// <summary>Native eye tracking (Quest Pro, OpenXR eye gaze). Phase 5, proposed default awaiting the product owner.</summary>
    public sealed class GazeConfig
    {
        /// <summary>A threshold presentation with gaze farther than this from the fixation target at stimulus onset is rejected.</summary>
        public double MaxErrorDeg { get; set; } = 3.0;
        /// <summary>Rejected presentations are not used by the strategy; the location is presented again.</summary>
        public bool RetestRejected { get; set; } = true;
        /// <summary>Gaze samples (one per presentation) are kept in the exam record for the report's gaze trace.</summary>
        public bool RecordTrace { get; set; } = true;
    }

    /// <summary>Mode B, oculokinetic fixation (VVP-like): the target jumps before each presentation. Phase 5, proposed default awaiting the product owner.</summary>
    public sealed class OculokineticConfig
    {
        /// <summary>Target positions are drawn uniformly in [-range, +range] on both axes (degrees from straight ahead).</summary>
        public double TargetRangeDeg { get; set; } = 10;
        /// <summary>Composed stimulus direction must stay within this eccentricity from straight ahead (usable field of the headset).</summary>
        public double MaxEccentricityDeg { get; set; } = 45;
        /// <summary>Without eye tracking: time the target is shown at its new position (with the "lock" animation) before the stimulus.</summary>
        public int LockDelayMs { get; set; } = 600;
        /// <summary>With eye tracking: gaze must stay within <see cref="GazeConfirmDeg"/> of the target for this long before the stimulus.</summary>
        public int GazeConfirmMs { get; set; } = 300;
        public double GazeConfirmDeg { get; set; } = 3;
        /// <summary>Heijl-Krakau blind-spot catch trials (relative to the current target) also in this mode.</summary>
        public bool HeijlKrakau { get; set; } = true;
    }

    public sealed class ReliabilityConfig
    {
        public CatchTrialConfig FalsePositive { get; set; }
        public CatchTrialConfig FalseNegative { get; set; }
        public CatchTrialConfig FixationLoss { get; set; }
        public BlindSpotConfig BlindSpot { get; set; }
        public GazeConfig Gaze { get; set; } = new GazeConfig();
        public OculokineticConfig Oculokinetic { get; set; } = new OculokineticConfig();
    }

    public sealed class LikelihoodSdConfig
    {
        /// <summary>"fixed" (OPI default, constant sd) or "henson" (sd = min(cap, exp(A*t+B)) on the HFA-equivalent scale).</summary>
        public string Type { get; set; }
        public double Sd { get; set; }
        public double HensonA { get; set; } = -0.081;
        public double HensonB { get; set; } = 3.27;
        public double HensonCap { get; set; } = 6.0;
    }

    public sealed class ZestPriorConfig
    {
        public double NormalWeight { get; set; }
        public double NormalSdDb { get; set; }
        public double AbnormalWeight { get; set; }
        public double AbnormalMeanHfa { get; set; }
        public double AbnormalSdDb { get; set; }
        public double Floor { get; set; }
    }

    public sealed class ZestConfig
    {
        public double DomainMinDb { get; set; }
        public double DomainMaxDb { get; set; }
        public double DomainStepDb { get; set; }
        public string StopType { get; set; }
        public double StopValue { get; set; }
        public int MaxSeenLimit { get; set; }
        public int MinNotSeenLimit { get; set; }
        public int MaxPresentations { get; set; }
        public string StimChoice { get; set; }
        public double FalsePositiveRate { get; set; }
        public double FalseNegativeRate { get; set; }
        public LikelihoodSdConfig LikelihoodSd { get; set; }
        public ZestPriorConfig Prior { get; set; }
    }

    public sealed class FullThresholdConfig
    {
        public double RetestIfDiffersByDb { get; set; }
        public bool UseSecondStaircase { get; set; }
    }

    public sealed class ScreeningConfig
    {
        public double BrighterThanExpectedByDb { get; set; }
        public bool RetestNotSeen { get; set; }
    }

    public sealed class GrowthConfig
    {
        public bool UseNeighbourSeeding { get; set; }
    }

    /// <summary>Optional foveal threshold before the main test (HFA "Fovea: ON"). Phase 5, proposed default awaiting the product owner.</summary>
    public sealed class FovealConfig
    {
        /// <summary>"Diamond4": four small points around the centre; the patient looks at the (empty) centre where the stimulus appears.</summary>
        public string FixationTarget { get; set; } = "Diamond4";
        /// <summary>Distance from the centre to each diamond point (degrees).</summary>
        public double DiamondHalfSizeDeg { get; set; } = 1.5;
        /// <summary>Strategy used for the foveal point: "exam" = the exam's own strategy; or Zest, ZestFast, FullThreshold, FourTwo.</summary>
        public string Strategy { get; set; } = "exam";
    }

    /// <summary>Forced-choice response mode (experimental, SPEC §5.4): the patient reports in which of N intervals the stimulus appeared.</summary>
    public sealed class ForcedChoiceConfig
    {
        public int Intervals { get; set; } = 2;
        /// <summary>Each interval lasts this long; the stimulus fills one of them (stimulus duration = interval duration).</summary>
        public int IntervalMs { get; set; } = 200;
        public int GapMs { get; set; } = 600;
        /// <summary>Fixation-target blink that marks the start of each interval.</summary>
        public int MarkerMs { get; set; } = 100;
        /// <summary>ZEST likelihood floor: the chance level 1/N replaces the false-positive rate.</summary>
        public bool ZestGuessRate { get; set; } = true;
    }

    public sealed class StrategiesConfig
    {
        public double MinStimulusDb { get; set; }
        public double MaxStimulusDb { get; set; }
        public ZestConfig Zest { get; set; }
        public ZestConfig ZestFast { get; set; }
        public FullThresholdConfig FullThreshold { get; set; }
        public ScreeningConfig Screening { get; set; }
        public GrowthConfig Growth { get; set; }
        public FovealConfig Foveal { get; set; } = new FovealConfig();
        public ForcedChoiceConfig ForcedChoice { get; set; } = new ForcedChoiceConfig();
    }

    public sealed class GrayscaleConfig
    {
        public string Scale { get; set; }
        public List<double> EdgesDb { get; set; }
    }

    public sealed class VfiConfig
    {
        public double UsePdWhenMdAtLeastDb { get; set; }
        public double SignificanceLevel { get; set; }
        public double WeightA { get; set; }
        public double WeightB { get; set; }
    }

    public sealed class AnalysisConfig
    {
        public double GeneralHeightPercentile { get; set; }
        public string MdWeighting { get; set; }
        public List<double> ProbabilityLevels { get; set; }
        public VfiConfig Vfi { get; set; }
    }

    public sealed class GhtPointScore
    {
        public double P { get; set; }
        public double Score { get; set; }
    }

    public sealed class GhtSector
    {
        public string Name { get; set; }
        public List<double[]> Points { get; set; }
    }

    public sealed class GhtConfig
    {
        public string Status { get; set; }
        public List<GhtPointScore> PointScores { get; set; }
        public double OutsideNormalLimitsPercentile { get; set; }
        public double BorderlinePercentile { get; set; }
        public double GeneralReductionPercentile { get; set; }
        public double AbnormallyHighPercentile { get; set; }
        public List<GhtSector> Sectors { get; set; }
    }

    public sealed class LocalSdConfig
    {
        public double InterceptDb { get; set; }
        public double PerDegDb { get; set; }
    }

    public sealed class PopulationConfig
    {
        public int Size { get; set; }
        public double MinAge { get; set; }
        public double MaxAge { get; set; }
        public ulong Seed { get; set; }
    }

    public sealed class SyntheticNormativeConfig
    {
        public string Status { get; set; }
        public string Scale { get; set; }
        public double ReferenceAge { get; set; }
        public double PeakDb { get; set; }
        public double EccentricitySlopeDbPerDeg { get; set; }
        public double SuperiorExtraSlopeDbPerDeg { get; set; }
        public double AgeSlopeDbPerDecade { get; set; }
        public LocalSdConfig LocalSd { get; set; }
        public LocalSdConfig MeasurementSd { get; set; }
        /// <summary>Foveal sensitivity above the model value at eccentricity 0 (the hill of vision peaks at the fovea). Phase 5.</summary>
        public double FovealExtraDb { get; set; } = 1.5;
        public double GlobalShiftSdDb { get; set; }
        public PopulationConfig Population { get; set; }
    }

    /// <summary>Phase 6: rules for building the clinic's own normative database from a cohort of healthy eyes (config/normative/cohort.json).</summary>
    public sealed class NormativeCohortConfig
    {
        public double MinAgeYears { get; set; } = 18;
        public double? MaxAgeYears { get; set; }
        public bool ExcludeAborted { get; set; } = true;
        // Replace (not append to) the defaults when the file lists these.
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<string> AllowedStrategies { get; set; } = new List<string> { "Zest", "ZestFast", "FullThreshold" };
        public CohortReliabilityRule Reliability { get; set; } = new CohortReliabilityRule();
        public bool ExcludeFirstExamPerEye { get; set; } = true;
        /// <summary>"latest" (default) or "all": with "all" every eligible exam of an eye enters the population.</summary>
        public string OneExamPerEye { get; set; } = "latest";
        public int MinEyesForMeasured { get; set; } = 100;
        [JsonProperty(ObjectCreationHandling = ObjectCreationHandling.Replace)]
        public List<double[]> AgeBands { get; set; } = new List<double[]> { new[] { 18.0, 29 }, new[] { 30.0, 39 }, new[] { 40.0, 49 }, new[] { 50.0, 59 }, new[] { 60.0, 69 }, new[] { 70.0, 79 }, new[] { 80.0, 120 } };
        public double AgeRangeMarginYears { get; set; } = 5;
    }

    public sealed class CohortReliabilityRule
    {
        public bool UseAlertLimits { get; set; } = true;
    }

    public sealed class GridPointConfig
    {
        public double X { get; set; }
        public double Y { get; set; }
        public bool BlindSpot { get; set; }
    }

    public sealed class GridAnchorConfig
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public sealed class GridConfig
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string EyeConvention { get; set; }
        public double NeighbourRadiusDeg { get; set; }
        public List<GridAnchorConfig> Anchors { get; set; }
        public string Source { get; set; }
        /// <summary>False for grids whose points do not cover the GHT sectors (10-2): the report shows "not applicable".</summary>
        public bool GhtApplicable { get; set; } = true;
        public List<GridPointConfig> Points { get; set; }
    }
}
