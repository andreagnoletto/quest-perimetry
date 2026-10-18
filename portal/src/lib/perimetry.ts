import { execFile } from "node:child_process";
import { mkdtemp, rm, writeFile } from "node:fs/promises";
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

/** Runs the Single Field Analysis on a headset exam file (raw text, exactly as exported). */
export async function analyzeExam(rawText: string): Promise<Report> {
  return withTempFile(rawText, async (p) => {
    const r = await run(["report", p]);
    let doc: unknown;
    try { doc = JSON.parse(r.stdout); } catch { throw new PerimetryError("análise falhou: " + (r.stderr || r.stdout).slice(0, 500)); }
    const d = doc as { error?: string; schema?: string };
    if (d.error) throw new PerimetryError(d.error);
    if (d.schema !== "perimetry-report/1") throw new PerimetryError("resposta inesperada do /core");
    return doc as Report;
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
