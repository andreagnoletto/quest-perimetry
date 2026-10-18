import { readdirSync, readFileSync } from "node:fs";
import path from "node:path";
import { db } from "./db.ts";
import { env } from "./env.ts";
import { fixationModes, responseModes } from "./format.ts";

/** Exam presets the headset knows (config/headset.json). The portal only offers what the headset can run. */
export interface Preset { id: string; label: string; gridId: string; strategy: string; fixationMode: string; stimulusSize: string; eyes: string[] }

export function presets(): Preset[] {
  const raw = JSON.parse(readFileSync(path.join(env.configDir, "headset.json"), "utf8")) as { presets: Preset[] };
  return raw.presets;
}

/** Test grids defined in config/grids/*.json (ids as /core names them: 24-2, 30-2, 10-2, 24-2C), 24-2 first. */
export function gridIds(): string[] {
  const dir = path.join(env.configDir, "grids");
  const ids = readdirSync(dir).filter((f) => f.endsWith(".json")).map((f) => (JSON.parse(readFileSync(path.join(dir, f), "utf8")) as { id: string }).id);
  const order = ["24-2", "30-2", "10-2", "24-2C"];
  return ids.sort((a, b) => (order.indexOf(a) + 1 || 99) - (order.indexOf(b) + 1 || 99));
}

/** Number of test locations of a grid (config/grids/<id>.json), for cohort rows of exams without analysis. */
export function gridPointCount(gridId: string): number {
  const dir = path.join(env.configDir, "grids");
  for (const f of readdirSync(dir).filter((f) => f.endsWith(".json"))) {
    const g = JSON.parse(readFileSync(path.join(dir, f), "utf8")) as { id: string; points: unknown[] };
    if (g.id === gridId) return g.points.length;
  }
  throw new Error(`grade desconhecida: ${gridId}`);
}

/** Phase 5 options of an order; null/undefined = not set (the headset keeps the preset's value or the default). */
export interface OrderOptions {
  gridId?: string | null;
  fixationMode?: string | null;
  fovealThreshold?: boolean | null;
  responseMode?: string | null;
}

export function isFixationMode(v: string): v is (typeof fixationModes)[number] { return (fixationModes as readonly string[]).includes(v); }
export function isResponseMode(v: string): v is (typeof responseModes)[number] { return (responseModes as readonly string[]).includes(v); }

/**
 * exam-order.json read by headset PerimetryApp (ExamOrder): pseudonymous code only, never the name. The Phase 5
 * keys (`grid`, `fixationMode`, `fovealThreshold`, `responseMode`) are optional and override the preset
 * (docs/SYNC_API.md); they are written only when set, so older headsets ignore nothing they did not expect.
 */
export function examOrderFile(o: { code: string; ageYears: number; refraction: string | null; presetId: string; seed?: string | null } & OrderOptions) {
  return {
    patientRef: o.code,
    ageYears: Math.round(o.ageYears * 10) / 10,
    refractiveCorrection: o.refraction ?? null,
    presetId: o.presetId,
    ...(o.seed ? { seed: Number(o.seed) } : {}),
    ...(o.gridId ? { grid: o.gridId } : {}),
    ...(o.fixationMode ? { fixationMode: o.fixationMode } : {}),
    ...(o.fovealThreshold ? { fovealThreshold: true } : {}),
    ...(o.responseMode && o.responseMode !== "YesNo" ? { responseMode: o.responseMode } : {}),
  };
}

/** Headsets with an active sync token (docs/SYNC_API.md), for the "send to headset" choice on an order. */
export async function pairedDevices(): Promise<{ id: string; label: string }[]> {
  const rows = await db.device.findMany({ where: { tokens: { some: { revokedAt: null } } }, orderBy: { createdAt: "asc" } });
  return rows.map((d) => ({ id: d.id, label: d.label ?? d.model ?? d.serial }));
}
