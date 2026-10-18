"use server";

import { revalidatePath } from "next/cache";
import { z } from "zod";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { encrypt } from "@/lib/crypto";
import { db } from "@/lib/db";
import { checkCalibration } from "@/lib/perimetry";

export type DeviceState = { error?: string; ok?: string } | undefined;

const deviceSchema = z.object({
  serial: z.string().trim().min(1, "Informe o identificador do headset").max(200),
  label: z.string().trim().max(100).optional(),
  model: z.string().trim().max(100).optional(),
});

export async function saveDeviceAction(_: DeviceState, form: FormData): Promise<DeviceState> {
  const user = await requireUser(["admin", "doctor"]);
  const parsed = deviceSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { error: parsed.error.issues[0]?.message };
  const { serial, label, model } = parsed.data;
  const key = String(form.get("hmacKey") ?? "").trim();
  if (key && Buffer.from(key, "base64").length < 16) return { error: "Chave inválida: cole o conteúdo do arquivo device.key do headset (base64)." };
  const d = await db.device.upsert({
    where: { serial },
    update: { label: label || undefined, model: model || undefined, ...(key ? { hmacKeyEnc: encrypt(key) } : {}) },
    create: { serial, label: label || null, model: model || null, hmacKeyEnc: key ? encrypt(key) : null },
  });
  await audit(user.id, key ? "device.key" : "device.save", "device", d.id, serial.slice(0, 40));
  revalidatePath("/devices");
  return { ok: key ? "Headset salvo com a chave de integridade." : "Headset salvo." };
}

export async function uploadCalibrationAction(deviceId: string, _: DeviceState, form: FormData): Promise<DeviceState> {
  const user = await requireUser(["admin", "doctor"]);
  const file = form.get("file");
  if (!(file instanceof File) || file.size === 0) return { error: "Escolha o arquivo de calibração (JSON)." };
  if (file.size > 2 * 1024 * 1024) return { error: "Arquivo grande demais para uma calibração." };
  const device = await db.device.findUnique({ where: { id: deviceId } });
  if (!device) return { error: "Headset não encontrado." };
  const json = await file.text();
  const c = await checkCalibration(json);
  if (!c.ok) return { error: "Calibração inválida: " + (c.error ?? "erro desconhecido") };
  if (!c.IsMeasured) return { error: "O arquivo não tem medições fotométricas (é o modelo nominal)." };
  if (c.DeviceSerial && c.DeviceSerial !== device.serial) return { error: `A calibração é do headset ${c.DeviceSerial}, não deste (${device.serial}).` };
  if (c.missingEyes?.length) return { error: `Faltam medições do(s) olho(s) ${c.missingEyes.join(", ")}.` };
  const cal = await db.$transaction(async (tx) => {
    await tx.calibration.updateMany({ where: { deviceId, active: true }, data: { active: false } });
    return tx.calibration.create({
      data: {
        deviceId, json, summary: c as object, uploadedById: user.id, photometer: c.Photometer ?? null,
        measuredAt: c.MeasuredAtUtc ? new Date(c.MeasuredAtUtc) : null, validUntil: c.ValidUntilUtc ? new Date(c.ValidUntilUtc) : null,
      },
    });
  });
  await audit(user.id, "calibration.upload", "calibration", cal.id, device.serial.slice(0, 40));
  revalidatePath("/devices");
  return { ok: "Calibração validada e ativada. Baixe o calibration.json e copie para o headset." };
}

export type PairingState = { error?: string; code?: string; pairingId?: string; expiresAt?: string } | undefined;

/** New pairing code for a headset (docs/SYNC_API.md). Shown once; the headset exchanges it for a token. */
export async function createPairingAction(_: PairingState): Promise<PairingState> {
  const user = await requireUser(["admin", "doctor"]);
  const { newPairingCode, codeHash, PAIRING_TTL_MIN } = await import("@/lib/headset-auth");
  const code = newPairingCode();
  const expiresAt = new Date(Date.now() + PAIRING_TTL_MIN * 60_000);
  const pc = await db.pairingCode.create({ data: { codeHash: codeHash(code), createdById: user.id, expiresAt } });
  await audit(user.id, "device.pairing.create", "pairingCode", pc.id);
  return { code, pairingId: pc.id, expiresAt: expiresAt.toISOString() };
}

export async function revokeTokenAction(tokenId: string): Promise<void> {
  const user = await requireUser(["admin", "doctor"]);
  const t = await db.deviceToken.findUnique({ where: { id: tokenId }, include: { device: true } });
  if (!t || t.revokedAt) return;
  await db.deviceToken.update({ where: { id: tokenId }, data: { revokedAt: new Date() } });
  await audit(user.id, "device.token.revoke", "device", t.deviceId, t.device.serial.slice(0, 40));
  revalidatePath("/devices");
}
