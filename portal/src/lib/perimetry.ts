import { execFile } from "node:child_process";
import { mkdtemp, readFile, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { env } from "./env.ts";
import type { Report } from "./report-types.ts";

// Bridge to the /core CLI. The portal never computes clinical numbers itself: thresholds, TD/PD,
// probabilities, MD/PSD/VFI/GHT and warnings all come from `perimetry report`.

function run(args: string[], timeoutMs = 60_000): Promise<{ code: number; stdout: string; stderr: string }> {
  const [cmd, ...pre] = env.cli;
  return new Promise((resolve) => {
    execFile(cmd, [...pre, ...args, "--config", env.configDir], { timeout: timeoutMs, maxBuffer: 64 * 1024 * 1024 },
      (err, stdout, stderr) => {
        const exit = (err as { code?: unknown } | null)?.code;
        const code = err ? (typeof exit === "number" ? exit : 1) : 0;
        resolve({ code, stdout: String(stdout), stderr: String(stderr) });
      });
  });
}

async function withTempFile<T>(content: string, fn: (p: string) => Promise<T>): Promise<T> {
  const dir = await mkdtemp(path.join(tmpdir(), "perimetry-"));
  const p = path.join(dir, "input.json");
  try {
    await writeFile(p, content, "utf8");
    return await fn(p);
  } finally {
    await rm(dir, { recursive: true, force: true });
  }
}

export class PerimetryError extends Error {}

async function withTempDir<T>(fn: (dir: string) => Promise<T>): Promise<T> {
  const dir = await mkdtemp(path.join(tmpdir(), "perimetry-"));
  try { return await fn(dir); } finally { await rm(dir, { recursive: true, force: true }); }
}

function parseStrict<T>(r: { stdout: string; stderr: string }, schema: string): T {
  let doc: unknown;
  try { doc = JSON.parse(r.stdout); } catch { throw new PerimetryError("análise falhou: " + (r.stderr || r.stdout).slice(0, 500)); }
  const d = doc as { error?: string; schema?: string };
  if (d.error) throw new PerimetryError(d.error);
  if (d.schema !== schema) throw new PerimetryError("resposta inesperada do /core");
  return doc as T;
}

/**
 * Runs the Single Field Analysis on a headset exam file (raw text, exactly as exported).
 * `normativeJson` (Phase 6) is a perimetry-normative/1 database: the clinic's own normative instead of the
 * provisional synthetic one.
 */
export async function analyzeExam(rawText: string, normativeJson?: string | null): Promise<Report> {
  return withTempDir(async (dir) => {
    const p = path.join(dir, "input.json");
    await writeFile(p, rawText, "utf8");
    const args = ["report", p];
    if (normativeJson) {
      const n = path.join(dir, "normative.json");
      await writeFile(n, normativeJson, "utf8");
      args.push("--normative", n);
    }
    return parseStrict<Report>(await run(args), "perimetry-report/1");
  });
}

// ---------------------------------------------------------------------------------------------------------
// Phase 6: the clinic's own normative database. The portal only writes the cohort file (lib/normative.ts);
// every inclusion rule and statistic comes from `perimetry normative` (config/normative/cohort.json).

export interface CohortExclusion { SubjectId: string; ExamId: string; Eye: string; Reason: string; Detail: string | null }
export interface AgeBandStats { Band: string; MinAge: number; MaxAge: number; Eyes: number; Subjects: number; MeanAge: number; MeanSensitivityHfaDb: number | null; SdSensitivityHfaDb: number | null; EyesWithFovea: number }
export interface NormativeDatabaseMeta {
  path: string; id: string; gridId: string; status: "MEASURED" | "PROVISIONAL"; statusReason: string | null; source: string;
  cohortHash: string; builder: string; builtUtc: string; populationEyes: number; minEyesForMeasured: number; ageRange: [number | null, number | null];
  locations: number; foveal: { fromCohort: boolean; populationEyes: number; hasData: boolean };
}
/** Output of `perimetry normative stats|build` (schema perimetry-normative-build/1). */
export interface NormativeSummary {
  schema: "perimetry-normative-build/1";
  gridId: string;
  cohortHash: string;
  rules: Record<string, unknown>;
  configHash: string;
  candidates: number;
  included: number;
  includedExams: { SubjectId: string; Eye: string; ExamId: string; SequenceForEye: number; AgeYears: number; HasFovea: boolean }[];
  excluded: CohortExclusion[];
  exclusionsByReason: { Reason: string; Count: number }[];
  subjects: number;
  eyesOd: number;
  eyesOs: number;
  eyesWithFovea: number;
  ageRange: [number | null, number | null];
  eyesByBand: AgeBandStats[];
  status: "MEASURED" | "PROVISIONAL";
  statusReason: string | null;
  warnings: string[];
  database: NormativeDatabaseMeta | null;
}

/** `perimetry normative stats`: who would be included / excluded and why, without building anything. */
export async function normativeStats(cohortJson: string, gridId: string): Promise<NormativeSummary> {
  return withTempDir(async (dir) => {
    const c = path.join(dir, "cohort.json");
    await writeFile(c, cohortJson, "utf8");
    return parseStrict<NormativeSummary>(await run(["normative", "stats", "--cohort", c, "--grid", gridId], 120_000), "perimetry-normative-build/1");
  });
}

/** `perimetry normative build`: the summary plus the database file text (perimetry-normative/1) to store. */
export async function normativeBuild(cohortJson: string, gridId: string, id: string): Promise<{ summary: NormativeSummary; databaseJson: string }> {
  return withTempDir(async (dir) => {
    const c = path.join(dir, "cohort.json"), out = path.join(dir, "db.json");
    await writeFile(c, cohortJson, "utf8");
    const summary = parseStrict<NormativeSummary>(await run(["normative", "build", "--cohort", c, "--grid", gridId, "--id", id, "--out", out], 300_000), "perimetry-normative-build/1");
    return { summary, databaseJson: await readFile(out, "utf8") };
  });
}

export interface CalibrationCheck {
  ok: boolean;
  error?: string;
  DeviceSerial?: string;
  Photometer?: string;
  MeasuredAtUtc?: string | null;
  ValidUntilUtc?: string | null;
  IsMeasured?: boolean;
  eyes?: Record<string, { samples: number; minCdm2: number; maxCdm2: number }>;
  missingEyes?: string[];
}

/** Validates a photometric calibration with the same code the headset uses to load it. */
export async function checkCalibration(json: string): Promise<CalibrationCheck> {
  return withTempFile(json, async (p) => {
    const r = await run(["check-calibration", p]);
    try { return JSON.parse(r.stdout) as CalibrationCheck; } catch { return { ok: false, error: (r.stderr || "falha ao validar").slice(0, 500) }; }
  });
}

/** Parses a headset JSON file. Older CLI output wrote bare NaN; it is mapped to null. */
export function parseHeadsetJson(text: string): unknown {
  try { return JSON.parse(text); } catch {
    return JSON.parse(text.replace(/(:\s*|\[\s*|,\s*)(-?Infinity|NaN)(?=\s*[,\]}])/g, "$1null"));
  }
}
