using System.Collections.Generic;

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

    public sealed class ReliabilityConfig
    {
        public CatchTrialConfig FalsePositive { get; set; }
        public CatchTrialConfig FalseNegative { get; set; }
        public CatchTrialConfig FixationLoss { get; set; }
        public BlindSpotConfig BlindSpot { get; set; }
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

    public sealed class StrategiesConfig
    {
        public double MinStimulusDb { get; set; }
        public double MaxStimulusDb { get; set; }
        public ZestConfig Zest { get; set; }
        public ZestConfig ZestFast { get; set; }
        public FullThresholdConfig FullThreshold { get; set; }
        public ScreeningConfig Screening { get; set; }
        public GrowthConfig Growth { get; set; }
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
        public double GlobalShiftSdDb { get; set; }
        public PopulationConfig Population { get; set; }
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
        public List<GridPointConfig> Points { get; set; }
    }
}
