import { randomBytes } from "node:crypto";
import { describe, expect, it } from "vitest";
import type { Exam, ExamOrder, Patient } from "@prisma/client";

// view() decrypts with the server key, so a throwaway key is set before the modules load
process.env.PERIMETRY_DATA_KEY = randomBytes(32).toString("base64");
const { blindIndex, encrypt } = await import("@/lib/crypto");
const { ANON_MARKER, anonymizedFields, confirmsCode, isAnonymized, patientExport } = await import("@/lib/lgpd");
const { ANONYMIZED_NAME, ageAt, view } = await import("@/lib/patients");
const { CONSENT_VERSION } = await import("@/lib/consent-text");

const t0 = new Date("2026-10-18T12:00:00Z");

function patientRow(over: Partial<Patient> = {}): Patient {
  return {
    id: "pat1", code: "P-7K3M9Q", createdAt: t0, createdById: "u1",
    internalIdEnc: encrypt("PR-0001"), internalIdIdx: blindIndex("PR-0001", "internalId"),
    nameEnc: encrypt("Maria Conceição da Silva"), nameIdx: blindIndex("Maria Conceição da Silva", "name"),
    birthDateEnc: encrypt("1963-05-20"), sexEnc: encrypt("F"), refractionEnc: encrypt("+2,00 esf"), diagnosisEnc: null,
    consentAt: t0, consentById: "u1", consentNote: "termo 12", consentVersion: CONSENT_VERSION,
    researchConsent: true, researchConsentAt: t0, researchConsentById: "u1",
    anonymizedAt: null, anonymizedById: null,
    ...over,
  } as Patient;
}

describe("anonymisation", () => {
  it("replaces every identifying field with markers and withdraws research consent", () => {
    const f = anonymizedFields("pat1", "u2", t0);
    expect(f.nameEnc).toBe(ANON_MARKER);
    expect(f.internalIdEnc).toBe(ANON_MARKER);
    expect(f.birthDateEnc).toBe(ANON_MARKER);
    expect(f.nameIdx).toBe("anonymized:pat1");
    expect(f.internalIdIdx).toBe("anonymized:pat1"); // unique per patient so the unique index holds
    expect(f.sexEnc).toBeNull(); expect(f.refractionEnc).toBeNull(); expect(f.diagnosisEnc).toBeNull(); expect(f.consentNote).toBeNull();
    expect(f.researchConsent).toBe(false);
    expect(f.anonymizedAt).toBe(t0); expect(f.anonymizedById).toBe("u2");
    // no marker can collide with a real blind index (hex) or a real search
    expect(f.nameIdx).not.toBe(blindIndex("Maria Conceição da Silva", "name"));
  });
  it("the view of an anonymised row never decrypts and shows the placeholder", () => {
    const row = patientRow({ ...anonymizedFields("pat1", "u2", t0) });
    expect(isAnonymized(row)).toBe(true);
    const v = view(row);
    expect(v.name).toBe(ANONYMIZED_NAME);
    expect(v.birthDate).toBe("");
    expect(v.sex).toBeNull(); expect(v.refraction).toBeNull(); expect(v.researchConsent).toBe(false);
    expect(v.code).toBe("P-7K3M9Q"); // the code stays, it is what links the exams
    expect(Number.isNaN(ageAt(v.birthDate, t0))).toBe(true);
  });
  it("needs the exact code typed, ignoring case and spaces", () => {
    expect(confirmsCode(" p-7k3m9q ", "P-7K3M9Q")).toBe(true);
    expect(confirmsCode("P-7K3M9", "P-7K3M9Q")).toBe(false);
    expect(confirmsCode("", "P-7K3M9Q")).toBe(false);
  });
});

describe("patient export (right of access)", () => {
  it("contains the decrypted record, consent version, orders and exams with the raw file", () => {
    const row = patientRow();
    const order = { createdAt: t0, presetId: "24-2-zest", gridId: null, fixationMode: null, fovealThreshold: null, responseMode: null, fulfilledAt: t0, cancelledAt: null } as ExamOrder;
    const exam = { examId: "E1", startedUtc: t0, eye: "OD", gridId: "24-2", strategy: "ZestFast", aborted: false, integrity: "verified", md: -3.2, psd: 4.1, vfi: 91.3, ght: "Fora dos limites normais", report: { schema: "perimetry-report/1" }, rawText: "{\"ExamId\":\"E1\"}" } as unknown as Exam;
    const x = patientExport(row, [order], [exam], t0);
    expect(x.schema).toBe("perimetry-patient-export/1");
    expect(x.exportedUtc).toBe(t0.toISOString());
    expect(x.patient.name).toBe("Maria Conceição da Silva");
    expect(x.patient.internalId).toBe("PR-0001");
    expect(x.patient.birthDate).toBe("1963-05-20");
    expect(x.consent).toEqual({ version: CONSENT_VERSION, currentVersion: CONSENT_VERSION, at: t0, note: "termo 12", research: true, researchAt: t0 });
    expect(x.orders).toHaveLength(1);
    expect(x.exams[0]).toMatchObject({ examId: "E1", eye: "OD", md: -3.2, rawHeadsetFile: "{\"ExamId\":\"E1\"}" });
    expect(JSON.stringify(x)).not.toContain("Enc"); // no ciphertext field leaks into the file
  });
});
