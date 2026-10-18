import { createHash, randomBytes } from "node:crypto";
import type { Device } from "@prisma/client";
import { NextResponse } from "next/server";
import { db } from "./db.ts";

// Headset pairing and bearer tokens (docs/SYNC_API.md). A pairing code is shown once to the doctor and stored
// hashed; the headset exchanges it for a token whose SHA-256 is the DeviceToken id. Nothing secret is stored
// in clear.

export const SYNC_SCHEMA = "perimetry-sync/1";
export const PAIRING_TTL_MIN = 15;
export const PAIRING_MAX_ATTEMPTS = 5;
const CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ";

export const sha256 = (s: string) => createHash("sha256").update(s).digest("hex");

/** "7K3M-9Q2X": 8 symbols of a 31-letter alphabet (≈ 8.5e11), displayed in two groups. */
export function newPairingCode(): string {
  const b = randomBytes(8);
  let s = "";
  for (let i = 0; i < 8; i++) s += CODE_ALPHABET[b[i] % CODE_ALPHABET.length];
  return s.slice(0, 4) + "-" + s.slice(4);
}

/** Pairing codes compare without hyphens/spaces and case-insensitively; empty when not 8 symbols. */
export function normaliseCode(code: string): string {
  const s = code.replace(/[\s-]/g, "").toUpperCase();
  return /^[2-9A-Z]{8}$/.test(s) ? s : "";
}

export const codeHash = (code: string) => sha256("pair:" + normaliseCode(code));

export function newToken(): { token: string; id: string } {
  const token = randomBytes(32).toString("base64url");
  return { token, id: sha256(token) };
}

export function syncJson(data: Record<string, unknown>, status = 200) {
  return NextResponse.json({ schema: SYNC_SCHEMA, ...data, serverTimeUtc: new Date().toISOString() }, { status, headers: { "cache-control": "no-store" } });
}

export function syncError(error: string, message: string, status: number) {
  return NextResponse.json({ schema: SYNC_SCHEMA, error, message }, { status, headers: { "cache-control": "no-store" } });
}

/** Device behind the Authorization header, or null (revoked and expired tokens included). Touches lastUsedAt at most once a minute. */
export async function authenticateDevice(req: Request): Promise<Device | null> {
  const h = req.headers.get("authorization") ?? "";
  const m = /^Bearer\s+([A-Za-z0-9_-]{20,200})$/.exec(h);
  if (!m) return null;
  const t = await db.deviceToken.findUnique({ where: { id: sha256(m[1]) }, include: { device: true } });
  if (!t || t.revokedAt) return null;
  if (t.expiresAt && t.expiresAt <= new Date()) return null;
  if (!t.lastUsedAt || Date.now() - t.lastUsedAt.getTime() > 60_000) {
    await db.deviceToken.update({ where: { id: t.id }, data: { lastUsedAt: new Date() } }).catch(() => undefined);
  }
  return t.device;
}

/** Access-log entry for a headset call (no user). */
export async function auditDevice(device: Device | null, action: string, entityType?: string, entityId?: string, detail?: string, req?: Request) {
  const ip = req?.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? req?.headers.get("x-real-ip") ?? null;
  await db.accessLog.create({ data: { userId: null, action, entityType, entityId, detail: [device?.serial, detail].filter(Boolean).join("; ").slice(0, 500) || null, ip } });
}

/** Reads a JSON body of at most `maxBytes`; null when absent, too large or malformed. */
export async function readJson(req: Request, maxBytes = 20 * 1024 * 1024): Promise<unknown | null> {
  const len = Number(req.headers.get("content-length") ?? 0);
  if (len > maxBytes) return null;
  try {
    const text = await req.text();
    if (text.length > maxBytes) return null;
    return JSON.parse(text);
  } catch {
    return null;
  }
}
