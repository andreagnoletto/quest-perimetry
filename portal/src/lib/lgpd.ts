import type { Exam, ExamOrder, Patient } from "@prisma/client";
import { CONSENT_VERSION } from "./consent-text.ts";
import { view, type PatientView } from "./patients.ts";

// LGPD data-subject rights (Phase 6): the right of access (art. 18 II, export) and the right to erasure
// (art. 18 VI), implemented as anonymisation: the clinical record (exams, reports, raw headset files) stays
// under the pseudonymous code, which the headset never links to a person, and every identifying field is
// replaced by a marker. Nothing is deleted, so an exam can still be re-verified and audited.

export const ANON_MARKER = "anonymized";

/** Fields of the update that anonymises a patient row. The blind indexes get unique markers so the unique
 *  constraint holds and no search can match them. */
export function anonymizedFields(patientId: string, byUserId: string, at = new Date()) {
  return {
    nameEnc: ANON_MARKER,
    nameIdx: `${ANON_MARKER}:${patientId}`,
    internalIdEnc: ANON_MARKER,
    internalIdIdx: `${ANON_MARKER}:${patientId}`,
    birthDateEnc: ANON_MARKER,
    sexEnc: null,
    refractionEnc: null,
    diagnosisEnc: null,
    consentNote: null,
    researchConsent: false,
    anonymizedAt: at,
    anonymizedById: byUserId,
  };
}

export const isAnonymized = (p: { anonymizedAt: Date | null }) => p.anonymizedAt !== null;

/** Typed confirmation: the user must type the patient's pseudonymous code exactly (case-insensitive). */
export function confirmsCode(typed: string, code: string): boolean {
  return typed.trim().toUpperCase() === code.toUpperCase();
}

export interface PatientExport {
  schema: "perimetry-patient-export/1";
  exportedUtc: string;
  patient: PatientView & { consentVersion: string | null; anonymizedAt: Date | null };
  consent: { version: string | null; currentVersion: string; at: Date | null; note: string | null; research: boolean; researchAt: Date | null };
  orders: { createdAt: Date; presetId: string; gridId: string | null; fixationMode: string | null; fovealThreshold: boolean | null; responseMode: string | null; fulfilledAt: Date | null; cancelledAt: Date | null }[];
  exams: { examId: string; startedUtc: Date; eye: string; gridId: string; strategy: string; aborted: boolean; integrity: string; md: number | null; psd: number | null; vfi: number | null; ght: string | null; report: unknown; rawHeadsetFile: string }[];
}

/** Everything the portal holds about one patient, decrypted, for the right of access. */
export function patientExport(p: Patient, orders: ExamOrder[], exams: Exam[], now = new Date()): PatientExport {
  const v = view(p);
  return {
    schema: "perimetry-patient-export/1",
    exportedUtc: now.toISOString(),
    patient: { ...v, consentVersion: p.consentVersion, anonymizedAt: p.anonymizedAt },
    consent: { version: p.consentVersion, currentVersion: CONSENT_VERSION, at: p.consentAt, note: p.consentNote, research: p.researchConsent, researchAt: p.researchConsentAt },
    orders: orders.map((o) => ({ createdAt: o.createdAt, presetId: o.presetId, gridId: o.gridId, fixationMode: o.fixationMode, fovealThreshold: o.fovealThreshold, responseMode: o.responseMode, fulfilledAt: o.fulfilledAt, cancelledAt: o.cancelledAt })),
    exams: exams.map((e) => ({ examId: e.examId, startedUtc: e.startedUtc, eye: e.eye, gridId: e.gridId, strategy: e.strategy, aborted: e.aborted, integrity: e.integrity, md: e.md, psd: e.psd, vfi: e.vfi, ght: e.ght, report: e.report, rawHeadsetFile: e.rawText })),
  };
}
