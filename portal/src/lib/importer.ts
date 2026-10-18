import type { Integrity } from "@prisma/client";
import { decryptOpt } from "./crypto.ts";
import { db } from "./db.ts";
import { verifyHmac } from "./hmac.ts";
import { analyzeExam, parseHeadsetJson, PerimetryError } from "./perimetry.ts";
import type { Report } from "./report-types.ts";

// Import of the files the headset writes to exams/ (headset/README.md): exam_*.json and session_*.json,
// each with an optional ".hmac" sidecar. Every exam is analysed by /core on import and the raw bytes are
// kept, so the analysis can be redone and the HMAC re-checked later.

export interface UploadedFile { name: string; bytes: Buffer }

export interface ImportOutcome {
  file: string;
  kind: "exam" | "session" | "hmac" | "unknown";
  status: "imported" | "duplicate" | "error" | "skipped";
  message: string;
  examDbId?: string;
  patientCode?: string | null;
  integrity?: Integrity;
}

interface ExamLike { ExamId: string; StartedUtc: string; Settings: { Eye: string | number; GridId: string; Strategy: string | number; PatientRef?: string | null }; Locations: unknown[]; Aborted?: boolean; ConfigHash?: string; Calibrated?: boolean }
interface SessionLike { SessionId: string; StartedUtc: string; DeviceSerial?: string; DeviceModel?: string; ExamIds?: string[]; Outcome?: string; Order?: { Id?: string | null; PatientRef?: string | null } | null }

const EYES = ["OD", "OS"];
const STRATEGIES = ["Zest", "ZestFast", "FullThreshold", "FourTwo", "Screening"];
const enumName = (v: string | number, names: string[]) => (typeof v === "number" ? names[v] ?? String(v) : v);

function classify(doc: unknown): "exam" | "session" | "unknown" {
  const d = doc as Record<string, unknown>;
  if (d && typeof d === "object" && "Settings" in d && "Locations" in d && "ExamId" in d) return "exam";
  if (d && typeof d === "object" && "SessionId" in d && "Events" in d) return "session";
  return "unknown";
}

/**
 * Columns copied from the /core report for the lists and trends (the report itself stays the source of truth):
 * global indices, GHT, and the Phase 5 foveal threshold (HFA-equivalent dB) and gaze-tracking flag.
 */
export function examSummary(report: Report): { md: number | null; vfi: number | null; psd: number | null; ght: string | null; fovealHfaDb: number | null; gazeTracked: boolean } {
  const a = report.analysis;
  return {
    md: a?.Md ?? null, vfi: a?.Vfi ?? null, psd: a?.Psd ?? null, ght: a?.Ght ?? null,
    fovealHfaDb: report.exam.Foveal ? report.exam.Foveal.ThresholdHfaDb : report.exam.FovealThresholdHfaDb ?? null,
    gazeTracked: report.exam.Reliability.GazeTracked === true,
  };
}

function integrityOf(bytes: Buffer, sidecar: string | undefined, keyB64: string | null): Integrity {
  if (!sidecar || !keyB64) return "unverified";
  return verifyHmac(bytes, sidecar, keyB64) ? "verified" : "failed";
}

/** `userId` null = uploaded by the headset itself through the sync API. */
export async function importFiles(files: UploadedFile[], opts: { userId: string | null; deviceId?: string | null; patientId?: string | null }): Promise<ImportOutcome[]> {
  const out: ImportOutcome[] = [];
  const sidecars = new Map<string, string>();
  const docs: { f: UploadedFile; doc: unknown; kind: "exam" | "session" }[] = [];

  for (const f of files) {
    if (f.name.endsWith(".hmac")) { sidecars.set(f.name.slice(0, -5), f.bytes.toString("utf8")); out.push({ file: f.name, kind: "hmac", status: "skipped", message: "assinatura" }); continue; }
    let doc: unknown;
    try { doc = parseHeadsetJson(f.bytes.toString("utf8")); } catch { out.push({ file: f.name, kind: "unknown", status: "error", message: "não é JSON válido" }); continue; }
    const kind = classify(doc);
    if (kind === "unknown") { out.push({ file: f.name, kind, status: "error", message: "não é um arquivo de exame nem de sessão do headset" }); continue; }
    docs.push({ f, doc, kind });
  }

  // Device: chosen explicitly, else identified by the session log's serial (registered on first sight).
  let device = opts.deviceId ? await db.device.findUnique({ where: { id: opts.deviceId } }) : null;
  const sessionDoc = docs.find((d) => d.kind === "session")?.doc as SessionLike | undefined;
  if (!device && sessionDoc?.DeviceSerial && sessionDoc.DeviceSerial !== "unknown") {
    device = await db.device.upsert({
      where: { serial: sessionDoc.DeviceSerial },
      update: {},
      create: { serial: sessionDoc.DeviceSerial, model: sessionDoc.DeviceModel ?? null },
    });
  }
  const key = decryptOpt(device?.hmacKeyEnc);

  // Sessions first so exams can link to them.
  const sessionByExamId = new Map<string, string>();
  const orderByExamId = new Map<string, string>(); // order chosen on the headset (SessionLog.Order.Id, Phase 4)
  for (const d of docs.filter((x) => x.kind === "session")) {
    const s = d.doc as SessionLike;
    if (s.Order?.Id) for (const id of s.ExamIds ?? []) orderByExamId.set(id, s.Order.Id);
    const integrity = integrityOf(d.f.bytes, sidecars.get(d.f.name), key);
    const existing = await db.headsetSession.findUnique({ where: { sessionId: s.SessionId } });
    const row = existing ?? await db.headsetSession.create({
      data: {
        sessionId: s.SessionId, deviceId: device?.id ?? null, startedUtc: new Date(s.StartedUtc), outcome: s.Outcome ?? null,
        rawText: d.f.bytes.toString("utf8"), data: d.doc as object, integrity,
      },
    });
    for (const id of s.ExamIds ?? []) sessionByExamId.set(id, row.id);
    out.push({ file: d.f.name, kind: "session", status: existing ? "duplicate" : "imported", message: existing ? "sessão já importada" : `sessão ${s.Outcome ?? ""}`.trim(), integrity: row.integrity });
  }

  for (const d of docs.filter((x) => x.kind === "exam")) {
    const e = d.doc as ExamLike;
    const raw = d.f.bytes.toString("utf8");
    const startedUtc = new Date(e.StartedUtc);
    const dup = await db.exam.findUnique({ where: { examId_startedUtc: { examId: e.ExamId, startedUtc } } });
    if (dup) { out.push({ file: d.f.name, kind: "exam", status: "duplicate", message: "exame já importado", examDbId: dup.id }); continue; }

    let report;
    try { report = await analyzeExam(raw); } catch (err) {
      out.push({ file: d.f.name, kind: "exam", status: "error", message: err instanceof PerimetryError ? err.message : "falha na análise" });
      continue;
    }

    const ref = e.Settings.PatientRef ?? null;
    const patient = opts.patientId
      ? await db.patient.findUnique({ where: { id: opts.patientId } })
      : ref ? await db.patient.findUnique({ where: { code: ref } }) : null;
    const integrity = integrityOf(d.f.bytes, sidecars.get(d.f.name), key);
    const row = await db.exam.create({
      data: {
        examId: e.ExamId, startedUtc, patientId: patient?.id ?? null, sessionId: sessionByExamId.get(e.ExamId) ?? null,
        eye: enumName(e.Settings.Eye, EYES), gridId: e.Settings.GridId, strategy: enumName(e.Settings.Strategy, STRATEGIES),
        aborted: !!e.Aborted, configHash: e.ConfigHash ?? null, calibrated: !!e.Calibrated,
        rawText: raw, rawHmac: sidecars.get(d.f.name)?.trim() ?? null, integrity,
        report: report as unknown as object, analyzedConfig: report.analyzedWith.configHash,
        ...examSummary(report),
        importedById: opts.userId,
      },
    });
    const orderId = orderByExamId.get(e.ExamId);
    if (orderId) await db.examOrder.updateMany({ where: { id: orderId, fulfilledAt: null }, data: { fulfilledAt: new Date() } });
    else if (patient) {
      await db.examOrder.updateMany({ where: { patientId: patient.id, fulfilledAt: null, cancelledAt: null, createdAt: { lte: startedUtc } }, data: { fulfilledAt: new Date() } });
    }
    out.push({
      file: d.f.name, kind: "exam", status: "imported", examDbId: row.id, patientCode: patient?.code ?? null, integrity,
      message: patient ? `vinculado a ${patient.code}` : ref ? `código ${ref} sem paciente cadastrado — vincule manualmente` : "sem paciente — vincule manualmente",
    });
  }
  return out;
}
