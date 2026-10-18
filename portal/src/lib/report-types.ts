// Shape of `perimetry report` output (core/Perimetry.Cli/Report.cs, schema "perimetry-report/1").
// Numbers that are NaN in /core arrive as null.

export type Eye = "OD" | "OS";

export interface CatchCount { Trials: number; Errors: number; AlertLimit: number; Rate?: number | null; Alert?: boolean }

export interface PointAnalysis {
  Index: number;
  X: number; // OD convention
  Y: number;
  IsBlindSpot: boolean;
  DeviceDb: number | null;
  HfaEqDb: number | null;
  CensoredLow: boolean;
  ExpectedHfaDb: number | null;
  Td: number | null;
  TdP: number | null;
  Pd: number | null;
  PdP: number | null;
  GrayBand: number;
  ScreeningOutcome: "Seen" | "SeenOnRetest" | "NotSeen" | null;
}

export interface AnalysisResult {
  ExamId: string;
  Eye: Eye;
  GridId: string;
  AgeYears: number;
  Points: PointAnalysis[];
  Md: number | null;
  MdP: number | null;
  Psd: number | null;
  PsdP: number | null;
  GeneralHeight: number | null;
  Vfi: number | null;
  Ght: string;
  GhtSectorDifferences: (number | null)[];
  GhtStatus: string;
  Calibrated: boolean;
  NormativeProvisional: boolean;
  HfaOffsetDb: number;
  Warnings: string[];
}

export interface ExamSettings {
  GridId: string;
  Eye: Eye;
  Strategy: string;
  FixationMode: string;
  StimulusSize: string;
  AgeYears: number;
  Seed: number;
  FalsePositiveCatchTrials: boolean;
  FalseNegativeCatchTrials: boolean;
  PatientRef: string | null;
  RefractiveCorrection: string | null;
}

export interface ReportExam {
  ExamId: string;
  StartedUtc: string;
  SchemaVersion: string;
  SoftwareVersion: string;
  ConfigHash: string | null;
  Settings: ExamSettings;
  Calibrated: boolean;
  CalibrationRef: string | null;
  BackgroundCdm2: number;
  BackgroundAsb: number;
  MaxLuminanceCdm2: number;
  HfaOffsetDb: number;
  StimulusDiameterDeg: number;
  StimulusDurationMs: number;
  MinMeasurableHfaDb: number;
  NormativeId: string | null;
  NormativeProvisional: boolean;
  Aborted: boolean;
  Disclaimer: string;
  Reliability: {
    FixationLosses: CatchCount;
    FalsePositives: CatchCount;
    FalseNegatives: CatchCount;
    FixationLossRate: number | null;
    FalsePositiveRate: number | null;
    FalseNegativeRate: number | null;
    GazeRejectedPresentations: number;
    AnticipatedResponses: number;
    AbortedPresentations: number;
    BlindSpotFound: boolean;
    BlindSpotX: number | null;
    BlindSpotY: number | null;
    BlindSpotRemaps?: number;
    TotalPresentations: number;
    DurationMs: number;
    PausedMs: number;
    EffectiveDurationMs: number;
    Pauses: { StartMs: number; EndMs: number; Reason: string }[];
    LowReliability: boolean;
  };
  PresentationCount: number;
}

export interface Report {
  schema: "perimetry-report/1";
  analyzedWith: { software: string; configHash: string };
  exam: ReportExam;
  analysis: AnalysisResult | null;
  text: { ght: string; ghtEn: string } | null;
  warnings: string[];
}
