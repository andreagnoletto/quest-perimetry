import { decrypt, decryptOpt } from "./crypto.ts";
import { studyId } from "./normative.ts";
import { db } from "./db.ts";
import { ageAt } from "./patients.ts";
import type { Report } from "./report-types.ts";

// Pseudonymised research export (LGPD art. 13): only patients who authorised research use; no name,
// internal ID, birth date, headset code, free-text diagnosis or calendar dates. Each patient gets a
// study ID that cannot be linked back without the server key, and time is expressed as days since that
// patient's first exam. Anonymised patients (LGPD erasure) are out: consent withdrawn, age unknown.

const DAY = 86400_000;

export interface ResearchRow { studyId: string; seq: number; eye: string; daysFromFirst: number; ageYears: number; sex: string; report: Report; strategy: string; gridId: string; md: number | null; psd: number | null; vfi: number | null; integrity: string }

export async function researchRows(): Promise<ResearchRow[]> {
  const patients = await db.patient.findMany({
    where: { researchConsent: true, anonymizedAt: null },
    include: { exams: { where: { aborted: false }, orderBy: { startedUtc: "asc" } } },
  });
  const rows: ResearchRow[] = [];
  for (const p of patients) {
    if (!p.exams.length) continue;
    const sid = studyId(p.id);
    const birth = decrypt(p.birthDateEnc);
    const t0 = p.exams[0].startedUtc.getTime();
    p.exams.forEach((e, i) => rows.push({
      studyId: sid, seq: i + 1, eye: e.eye, daysFromFirst: Math.round((e.startedUtc.getTime() - t0) / DAY),
      ageYears: Math.floor(ageAt(birth, e.startedUtc)), sex: decryptOpt(p.sexEnc) ?? "",
      report: e.report as unknown as Report, strategy: e.strategy, gridId: e.gridId, md: e.md, psd: e.psd, vfi: e.vfi, integrity: e.integrity,
    }));
  }
  return rows;
}

const cell = (v: unknown) => {
  if (v === null || v === undefined) return "";
  const s = String(v);
  return /[",\n]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
};
const line = (xs: unknown[]) => xs.map(cell).join(",");

export function examsCsv(rows: ResearchRow[]): string {
  const head = ["study_id", "exam_seq", "eye", "days_from_first", "age_years", "sex", "grid", "strategy", "md_db", "md_p", "psd_db", "psd_p", "vfi_pct", "ght", "fl_rate", "fp_rate", "fn_rate", "low_reliability", "calibrated", "normative", "normative_provisional", "integrity", "config_hash"];
  return [line(head), ...rows.map((r) => {
    const e = r.report.exam, a = r.report.analysis, rel = e.Reliability;
    return line([r.studyId, r.seq, r.eye, r.daysFromFirst, r.ageYears, r.sex, r.gridId, r.strategy, r.md, a?.MdP, r.psd, a?.PsdP, r.vfi, a?.Ght, rel.FixationLossRate, rel.FalsePositiveRate, rel.FalseNegativeRate, rel.LowReliability, e.Calibrated, e.NormativeId, e.NormativeProvisional, r.integrity, r.report.analyzedWith.configHash]);
  })].join("\n") + "\n";
}

export function pointsCsvResearch(rows: ResearchRow[]): string {
  const head = ["study_id", "exam_seq", "eye", "point", "x_deg_od", "y_deg", "blind_spot", "device_db", "hfa_eq_db", "censored_low", "td_db", "td_p", "pd_db", "pd_p"];
  const out = [line(head)];
  for (const r of rows) for (const p of r.report.analysis?.Points ?? []) {
    out.push(line([r.studyId, r.seq, r.eye, p.Index, p.X, p.Y, p.IsBlindSpot, p.DeviceDb, p.HfaEqDb, p.CensoredLow, p.Td, p.TdP, p.Pd, p.PdP]));
  }
  return out.join("\n") + "\n";
}
