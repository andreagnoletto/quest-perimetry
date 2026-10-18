import type { Patient } from "@prisma/client";
import { blindIndex, decrypt, decryptOpt, encrypt, encryptOpt, randomCode } from "./crypto.ts";
import { db } from "./db.ts";

/** Decrypted patient for display. Only built inside authenticated, audited server code. */
export interface PatientView {
  id: string;
  code: string;
  internalId: string;
  name: string;
  birthDate: string; // YYYY-MM-DD
  sex: string | null;
  refraction: string | null;
  diagnosis: string | null;
  consentAt: Date | null;
  consentNote: string | null;
  researchConsent: boolean;
  /** Phase 6: healthy volunteer for the clinic's own normative database, and a free note (e.g. why excluded). */
  normativeVolunteer: boolean;
  normativeNote: string | null;
  createdAt: Date;
}

export function view(p: Patient): PatientView {
  return {
    id: p.id,
    code: p.code,
    internalId: decrypt(p.internalIdEnc),
    name: decrypt(p.nameEnc),
    birthDate: decrypt(p.birthDateEnc),
    sex: decryptOpt(p.sexEnc),
    refraction: decryptOpt(p.refractionEnc),
    diagnosis: decryptOpt(p.diagnosisEnc),
    consentAt: p.consentAt,
    consentNote: p.consentNote,
    researchConsent: p.researchConsent,
    normativeVolunteer: p.normativeVolunteer,
    normativeNote: decryptOpt(p.normativeNoteEnc),
    createdAt: p.createdAt,
  };
}

export interface PatientInput {
  internalId: string;
  name: string;
  birthDate: string;
  sex?: string | null;
  refraction?: string | null;
  diagnosis?: string | null;
  consent: boolean;
  consentNote?: string | null;
  researchConsent: boolean;
  normativeVolunteer?: boolean;
  normativeNote?: string | null;
}

export async function createPatient(input: PatientInput, userId: string): Promise<Patient> {
  for (let attempt = 0; ; attempt++) {
    try {
      return await db.patient.create({
        data: {
          code: randomCode(),
          internalIdEnc: encrypt(input.internalId.trim()),
          internalIdIdx: blindIndex(input.internalId, "internalId"),
          nameEnc: encrypt(input.name.trim()),
          nameIdx: blindIndex(input.name, "name"),
          birthDateEnc: encrypt(input.birthDate),
          sexEnc: encryptOpt(input.sex),
          refractionEnc: encryptOpt(input.refraction),
          diagnosisEnc: encryptOpt(input.diagnosis),
          consentAt: input.consent ? new Date() : null,
          consentById: input.consent ? userId : null,
          consentNote: input.consentNote || null,
          researchConsent: input.researchConsent,
          normativeVolunteer: !!input.normativeVolunteer,
          normativeNoteEnc: encryptOpt(input.normativeNote),
          createdById: userId,
        },
      });
    } catch (e) {
      const code = (e as { code?: string }).code;
      const target = String((e as { meta?: { target?: unknown } }).meta?.target ?? "");
      if (code === "P2002" && target.includes("internalIdIdx")) throw new Error("Já existe um paciente com esse ID interno");
      if (code === "P2002" && target.includes("code") && attempt < 5) continue; // pseudonym collision: draw again
      throw e;
    }
  }
}

export async function updatePatient(id: string, input: Omit<PatientInput, "consent" | "researchConsent" | "consentNote">) {
  return db.patient.update({
    where: { id },
    data: {
      normativeVolunteer: !!input.normativeVolunteer,
      normativeNoteEnc: encryptOpt(input.normativeNote),
      internalIdEnc: encrypt(input.internalId.trim()),
      internalIdIdx: blindIndex(input.internalId, "internalId"),
      nameEnc: encrypt(input.name.trim()),
      nameIdx: blindIndex(input.name, "name"),
      birthDateEnc: encrypt(input.birthDate),
      sexEnc: encryptOpt(input.sex),
      refractionEnc: encryptOpt(input.refraction),
      diagnosisEnc: encryptOpt(input.diagnosis),
    },
  });
}

/** Exact search by internal ID or full name (blind indexes), or by pseudonymous code. */
export async function findPatients(query: string): Promise<Patient[]> {
  const q = query.trim();
  if (!q) return db.patient.findMany({ orderBy: { createdAt: "desc" }, take: 200 });
  return db.patient.findMany({
    where: {
      OR: [
        { internalIdIdx: blindIndex(q, "internalId") },
        { nameIdx: blindIndex(q, "name") },
        { code: q.toUpperCase() },
      ],
    },
    orderBy: { createdAt: "desc" },
  });
}

/** Age in decimal years at a date (birth date as YYYY-MM-DD). */
export function ageAt(birthDate: string, at: Date): number {
  const b = new Date(birthDate + "T00:00:00Z");
  return (at.getTime() - b.getTime()) / (365.2425 * 24 * 3600 * 1000);
}
