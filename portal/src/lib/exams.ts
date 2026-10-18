import type { Exam } from "@prisma/client";
import { db } from "./db.ts";
import { audit } from "./audit.ts";
import { view } from "./patients.ts";
import type { Report } from "./report-types.ts";

export interface LoadedExam {
  exam: Exam;
  report: Report;
  patient: { id: string; name: string; internalId: string; birthDate: string; code: string } | null;
}

/** Loads one exam with its decrypted patient header and writes the access-log entry. */
export async function loadExam(id: string, userId: string, action: string): Promise<LoadedExam | null> {
  const exam = await db.exam.findUnique({ where: { id }, include: { patient: true } });
  if (!exam) return null;
  await audit(userId, action, "exam", id, exam.patient ? `patient ${exam.patient.code}` : "no patient");
  const { patient, ...rest } = exam;
  const p = patient ? view(patient) : null;
  return {
    exam: rest,
    report: exam.report as unknown as Report,
    patient: p ? { id: p.id, name: p.name, internalId: p.internalId, birthDate: p.birthDate, code: p.code } : null,
  };
}

/** Point table for CSV export (one row per test location, OD-convention coordinates as in /core). */
export function pointsCsv(r: Report): string {
  const head = ["index", "x_deg", "y_deg", "blind_spot", "device_db", "hfa_eq_db", "censored_low", "expected_hfa_db", "td_db", "td_p", "pd_db", "pd_p", "gray_band", "screening"];
  const cell = (v: unknown) => (v === null || v === undefined ? "" : String(v));
  const rows = (r.analysis?.Points ?? []).map((p) => [p.Index, p.X, p.Y, p.IsBlindSpot, p.DeviceDb, p.HfaEqDb, p.CensoredLow, p.ExpectedHfaDb, p.Td, p.TdP, p.Pd, p.PdP, p.GrayBand, p.ScreeningOutcome].map(cell).join(","));
  return [head.join(","), ...rows].join("\n") + "\n";
}
