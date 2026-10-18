// Live snapshot posted by the headset (docs/SYNC_API.md, POST /api/headset/live). Field names are what the
// headset's LiveSnapshot.cs writes. The portal shows these values and never derives clinical numbers from them.

export type LiveLocationState = "NotStarted" | "Active" | "Finished";

export interface LiveLocation {
  i: number;
  x: number; // OD convention, like ExamRecord.Locations
  y: number;
  blindSpot: boolean;
  state: LiveLocationState;
  db: number | null; // device dB
  hfaEqDb: number | null;
  censoredLow: boolean;
  presentations: number;
}

export interface LiveCatch { errors: number; trials: number }

export interface LiveReliability {
  fixationLosses: LiveCatch;
  falsePositives: LiveCatch;
  falseNegatives: LiveCatch;
  totalPresentations: number;
  blindSpotFound: boolean;
  blindSpotRemaps: number;
  durationMs: number;
  /** Phase 5 (optional): eye-tracking in use and presentations rejected for gaze error. */
  gazeTracked?: boolean;
  gazeRejected?: number;
}

export interface LiveLast { x: number; y: number; db: number | null; seen: boolean; kind: string; tMs: number }

export interface LiveExam {
  examId: string;
  gridId: string;
  strategy: string;
  eye: "OD" | "OS";
  hfaOffsetDb: number;
  minMeasurableHfaDb: number;
  locations: LiveLocation[];
  reliability: LiveReliability;
  last: LiveLast | null;
  /** Phase 5 (all optional, older headsets omit them). */
  fixationMode?: string;
  responseMode?: string;
  /** Foveal threshold in device dB once the foveal test finished; null while running / absent when off. */
  fovealThresholdDb?: number | null;
  fovealTest?: boolean;
}

export interface LiveSnapshot {
  schema: string;
  sessionId: string;
  seq: number;
  sentUtc: string;
  state: string;
  paused: boolean;
  finished: boolean;
  outcome: string | null;
  patientRef: string | null;
  orderId: string | null;
  presetId: string | null;
  ageYears: number | null;
  eye: "OD" | "OS" | null;
  startedUtc: string;
  elapsedMs: number;
  exam: LiveExam | null;
  ackCommandIds?: string[];
}

export type LiveCommandType = "pause" | "resume" | "abort";

export interface LiveCommandOut { id: string; type: LiveCommandType; issuedUtc: string; by: string }

/** What the browser receives over SSE and what the list page shows. */
export interface LiveView {
  sessionId: string;
  snapshot: LiveSnapshot;
  receivedUtc: string;
  deviceLabel: string | null;
  pendingCommands: LiveCommandOut[];
}

/** Sessions whose last snapshot is older than this are no longer "active" (headset offline or app closed). */
export const LIVE_ACTIVE_MS = 30_000;

export function isLiveSnapshot(v: unknown): v is LiveSnapshot {
  const s = v as Partial<LiveSnapshot> | null;
  return !!s && typeof s === "object" && typeof s.sessionId === "string" && /^[A-Za-z0-9_-]{4,64}$/.test(s.sessionId)
    && typeof s.seq === "number" && typeof s.state === "string" && typeof s.startedUtc === "string"
    && typeof s.finished === "boolean" && (s.exam === null || (typeof s.exam === "object" && Array.isArray((s.exam as LiveExam).locations)));
}

/** pt-BR label of a headset flow state for the live panel. */
export const liveStateName: Record<string, string> = {
  Welcome: "Boas-vindas", SetupOrder: "Operador: pedido", SetupPreset: "Operador: exame", SetupAge: "Operador: idade",
  Foveal: "Limiar foveal", FovealTest: "Limiar foveal",
  Instructions: "Instruções", EyeIntro: "Introdução do olho", SharpnessCheck: "Verificação de nitidez",
  OcclusionIntro: "Verificação de oclusão", OcclusionWindow: "Verificação de oclusão", OcclusionFailed: "Oclusão FALHOU",
  TrainingIntro: "Treino", TrainingTrial: "Treino", TrainingFeedback: "Treino", TrainingRepeat: "Treino (repetição)",
  ExamReady: "Pronto para o exame", Exam: "Exame em andamento", Paused: "Pausado", Resuming: "Retomando",
  BetweenEyes: "Descanso entre os olhos", Done: "Concluído", Aborted: "Interrompido", SaveError: "ERRO ao gravar",
};
