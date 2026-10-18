import { readFileSync } from "node:fs";
import path from "node:path";
import { db } from "./db.ts";
import { env } from "./env.ts";

/** Exam presets the headset knows (config/headset.json). The portal only offers what the headset can run. */
export interface Preset { id: string; label: string; gridId: string; strategy: string; fixationMode: string; stimulusSize: string; eyes: string[] }

export function presets(): Preset[] {
  const raw = JSON.parse(readFileSync(path.join(env.configDir, "headset.json"), "utf8")) as { presets: Preset[] };
  return raw.presets;
}

/** exam-order.json read by headset PerimetryApp (ExamOrder): pseudonymous code only, never the name. */
export function examOrderFile(o: { code: string; ageYears: number; refraction: string | null; presetId: string; seed?: string | null }) {
  return {
    patientRef: o.code,
    ageYears: Math.round(o.ageYears * 10) / 10,
    refractiveCorrection: o.refraction ?? null,
    presetId: o.presetId,
    ...(o.seed ? { seed: Number(o.seed) } : {}),
  };
}

/** Headsets with an active sync token (docs/SYNC_API.md), for the "send to headset" choice on an order. */
export async function pairedDevices(): Promise<{ id: string; label: string }[]> {
  const rows = await db.device.findMany({ where: { tokens: { some: { revokedAt: null } } }, orderBy: { createdAt: "asc" } });
  return rows.map((d) => ({ id: d.id, label: d.label ?? d.model ?? d.serial }));
}
