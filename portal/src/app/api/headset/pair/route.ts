import { z } from "zod";
import { encrypt } from "@/lib/crypto";
import { db } from "@/lib/db";
import { auditDevice, codeHash, newToken, normaliseCode, PAIRING_MAX_ATTEMPTS, readJson, syncError, syncJson } from "@/lib/headset-auth";
import { hit, PAIRING_IP_LIMIT } from "@/lib/rate-limit";

export const dynamic = "force-dynamic";

// POST /api/headset/pair — docs/SYNC_API.md. No token: the pairing code is the credential.
const schema = z.object({
  code: z.string().min(8).max(12),
  serial: z.string().trim().min(1).max(200),
  model: z.string().trim().max(100).optional().nullable(),
  hmacKey: z.string().trim().max(200).optional().nullable(),
  software: z.string().trim().max(100).optional().nullable(),
});

export async function POST(req: Request) {
  const ip = req.headers.get("x-forwarded-for")?.split(",")[0]?.trim() ?? req.headers.get("x-real-ip") ?? "local";
  if (!hit("pair:" + ip, PAIRING_IP_LIMIT.limit, PAIRING_IP_LIMIT.windowMs).allowed) {
    await auditDevice(null, "headset.pair.ratelimited", undefined, undefined, `ip ${ip.slice(0, 60)}`, req);
    return syncError("rate_limited", "muitas tentativas de pareamento deste endereço; aguarde 15 min", 429);
  }
  const body = await readJson(req, 64 * 1024);
  const parsed = schema.safeParse(body);
  if (!parsed.success) return syncError("bad_request", "code, serial e model são obrigatórios", 400);
  const { code, serial, model, hmacKey, software } = parsed.data;
  if (!normaliseCode(code)) return syncError("invalid_code", "código de pareamento inválido", 403);
  const now = new Date();

  const pc = await db.pairingCode.findUnique({ where: { codeHash: codeHash(code) } });
  if (!pc || pc.usedAt || pc.expiresAt < now || pc.attempts >= PAIRING_MAX_ATTEMPTS) {
    // count the miss on the newest live code so a guessed prefix cannot be brute-forced without limit
    await db.pairingCode.updateMany({ where: { usedAt: null, expiresAt: { gt: now } }, data: { attempts: { increment: 1 } } });
    await auditDevice(null, "headset.pair.denied", undefined, undefined, `serial ${serial.slice(0, 40)}`, req);
    return syncError("invalid_code", "código de pareamento inválido ou vencido", 403);
  }
  if (hmacKey && Buffer.from(hmacKey, "base64").length < 32) return syncError("bad_request", "hmacKey deve ser a chave device.key (base64, 32 bytes)", 400);

  const { token, id } = newToken();
  const device = await db.$transaction(async (tx) => {
    const d = await tx.device.upsert({
      where: { serial },
      update: { model: model || undefined, ...(hmacKey ? { hmacKeyEnc: encrypt(hmacKey) } : {}) },
      create: { serial, model: model || null, hmacKeyEnc: hmacKey ? encrypt(hmacKey) : null },
    });
    await tx.deviceToken.updateMany({ where: { deviceId: d.id, revokedAt: null }, data: { revokedAt: now } });
    await tx.deviceToken.create({ data: { id, deviceId: d.id, software: software || null } });
    await tx.pairingCode.update({ where: { id: pc.id }, data: { usedAt: now, deviceId: d.id } });
    return d;
  });
  await auditDevice(device, "headset.pair", "device", device.id, hmacKey ? "with hmac key" : "without hmac key", req);
  return syncJson({ deviceId: device.id, token, portalName: "Quest Perimetry" });
}
