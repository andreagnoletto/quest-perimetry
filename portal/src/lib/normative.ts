import type { Exam, NormativeSet } from "@prisma/client";
import { blindIndex, decrypt } from "./crypto.ts";
import { db } from "./db.ts";
import { gridPointCount } from "./headset.ts";
import { analysisColumns, analyzeWithActiveNormative } from "./importer.ts";
import { ageAt } from "./patients.ts";
import { normativeBuild, normativeStats, type NormativeSummary } from "./perimetry.ts";
import type { Report } from "./report-types.ts";

// Phase 6: the clinic's own normative database. The portal collects the candidate exams of the patients
// marked as healthy volunteers into a cohort file (schema perimetry-cohort/1) and hands it to
// `perimetry normative stats|build` (lib/perimetry.ts). Every inclusion rule (age, reliability, learning
// effect, one exam per eye, minimum population) and every statistic lives in /core and
// config/normative/cohort.json, never here. The cohort carries pseudonymous subject ids only: the same
// study id as the research export (lib/research.ts), never a name, internal id, birth date or patient code.

export const COHORT_SCHEMA = "perimetry-cohort/1";

/** Study id of a patient: pseudonymous, reproducible only with the server key. */
export function studyId(patientId: string): string {
  return "S-" + blindIndex(patientId, "research").slice(0, 12);
}

export interface CohortSubject {
  subjectId: string;
  eye: string;
  examId: string;
  sequenceForEye: number;
  ageYears: number;
  aborted: boolean;
  strategy: string;
  reliability: { fixationLossRate: number; falsePositiveRate: number; falseNegativeRate: number; gazeRejectedRate: number | null };
  thresholdsHfaDb: (number | null)[];
  fovealHfaDb: number | null;
}

export interface CohortFile { schema: typeof COHORT_SCHEMA; gridId: string; exportedUtc: string; subjects: CohortSubject[] }

/** What the cohort builder needs of an exam (a subset of the Exam row). */
export type CohortExam = Pick<Exam, "id" | "examId" | "eye" | "gridId" | "strategy" | "aborted" | "startedUtc"> & { report: unknown };
export interface CohortPatient { id: string; birthDate: string; exams: CohortExam[] }

/**
 * Builds the cohort file for one grid from volunteer patients. `sequenceForEye` counts that eye's exams of the
 * subject in chronological order (1 = first); the thresholds are the report's HFA-equivalent values in the
 * grid's own order (OD convention for both eyes, as /core stores them), null where the point has none
 * (blind-spot locations). An exam without analysis (interrupted) gets `pointCount` nulls so that /core reports
 * it as "aborted" rather than as a grid mismatch.
 */
export function cohortFromPatients(patients: CohortPatient[], gridId: string, pointCount: number, now = new Date()): CohortFile {
  const subjects: CohortSubject[] = [];
  for (const p of patients) {
    const sid = studyId(p.id);
    const exams = p.exams.filter((e) => e.gridId === gridId).slice().sort((a, b) => a.startedUtc.getTime() - b.startedUtc.getTime() || a.id.localeCompare(b.id));
    const seq: Record<string, number> = {};
    for (const e of exams) {
      seq[e.eye] = (seq[e.eye] ?? 0) + 1;
      const r = e.report as Report;
      const rel = r.exam?.Reliability;
      const gaze = rel?.GazeTracked && rel.GazeSamples ? rel.GazeRejectedPresentations / (rel.GazeSamples + rel.GazeRejectedPresentations) : null;
      subjects.push({
        subjectId: sid,
        eye: e.eye,
        examId: e.examId,
        sequenceForEye: seq[e.eye],
        ageYears: Math.round(ageAt(p.birthDate, e.startedUtc) * 100) / 100,
        aborted: e.aborted,
        strategy: e.strategy,
        reliability: {
          fixationLossRate: rel?.FixationLossRate ?? 0,
          falsePositiveRate: rel?.FalsePositiveRate ?? 0,
          falseNegativeRate: rel?.FalseNegativeRate ?? 0,
          gazeRejectedRate: gaze,
        },
        thresholdsHfaDb: r.analysis ? r.analysis.Points.map((pt) => (pt.IsBlindSpot ? null : pt.HfaEqDb)) : new Array<null>(pointCount).fill(null),
        fovealHfaDb: r.exam?.Foveal?.ThresholdHfaDb ?? r.exam?.FovealThresholdHfaDb ?? null,
      });
    }
  }
  return { schema: COHORT_SCHEMA, gridId, exportedUtc: now.toISOString(), subjects };
}

export interface GridCohort {
  file: CohortFile;
  json: string;
  volunteers: number;
  withoutResearchConsent: number;
  /** examId → exam row id, to link the included / excluded tables to the exams. */
  examLinks: Record<string, string>;
}

/** Loads every exam of every volunteer for a grid and builds the cohort file (only the birth date is decrypted, for the age). */
export async function cohortForGrid(gridId: string): Promise<GridCohort> {
  const rows = await db.patient.findMany({
    where: { normativeVolunteer: true, anonymizedAt: null },
    select: { id: true, birthDateEnc: true, researchConsent: true, exams: { where: { gridId }, select: { id: true, examId: true, eye: true, gridId: true, strategy: true, aborted: true, startedUtc: true, report: true } } },
  });
  const patients: CohortPatient[] = rows.map((p) => ({ id: p.id, birthDate: decrypt(p.birthDateEnc), exams: p.exams }));
  const file = cohortFromPatients(patients, gridId, gridPointCount(gridId));
  const examLinks: Record<string, string> = {};
  for (const p of rows) for (const e of p.exams) examLinks[e.examId] = e.id;
  return { file, json: JSON.stringify(file), volunteers: rows.length, withoutResearchConsent: rows.filter((p) => !p.researchConsent).length, examLinks };
}

/** `perimetry normative stats` on the current cohort of a grid. */
export async function cohortStats(gridId: string): Promise<{ cohort: GridCohort; stats: NormativeSummary }> {
  const cohort = await cohortForGrid(gridId);
  return { cohort, stats: await normativeStats(cohort.json, gridId) };
}

/** Builds the database from the current cohort of a grid and stores it (not active until `activateNormative`). */
export async function buildNormativeSet(gridId: string, userId: string): Promise<{ set: NormativeSet; summary: NormativeSummary }> {
  const cohort = await cohortForGrid(gridId);
  const day = new Date().toISOString().slice(0, 10);
  const sameDay = await db.normativeSet.count({ where: { gridId, builtAt: { gte: new Date(day + "T00:00:00Z") } } });
  const id = `clinic-${gridId}-${day}${sameDay ? "-" + (sameDay + 1) : ""}`;
  const { summary, databaseJson } = await normativeBuild(cohort.json, gridId, id);
  if (!summary.database) throw new Error("o /core não devolveu o banco construído");
  const set = await db.normativeSet.create({
    data: {
      gridId, label: id, status: summary.status, statusReason: summary.statusReason,
      populationEyes: summary.included, ageMin: summary.ageRange[0], ageMax: summary.ageRange[1],
      cohortHash: summary.cohortHash,
      summary: { ...summary, database: summary.database } as unknown as object,
      database: JSON.parse(databaseJson) as object,
      builtById: userId,
    },
  });
  return { set, summary };
}

/** Makes one set the active normative of its grid (and deactivates any other set of that grid). */
export async function activateNormative(setId: string, userId: string): Promise<NormativeSet> {
  const set = await db.normativeSet.findUniqueOrThrow({ where: { id: setId } });
  return db.$transaction(async (tx) => {
    await tx.normativeSet.updateMany({ where: { gridId: set.gridId, activatedAt: { not: null } }, data: { activatedAt: null, activatedById: null } });
    return tx.normativeSet.update({ where: { id: setId }, data: { activatedAt: new Date(), activatedById: userId } });
  });
}

export async function deactivateNormative(setId: string): Promise<NormativeSet> {
  return db.normativeSet.update({ where: { id: setId }, data: { activatedAt: null, activatedById: null } });
}

/** The active clinic normative for a grid, or null (= provisional synthetic). One active set per grid. */
export async function activeNormative(gridId: string): Promise<NormativeSet | null> {
  return db.normativeSet.findFirst({ where: { gridId, activatedAt: { not: null } }, orderBy: { activatedAt: "desc" } });
}

/** Re-analyses every exam of a grid with the normative active now (the clinic's set, or the provisional one). */
export async function reanalyzeGrid(gridId: string): Promise<{ total: number; reanalyzed: number; failed: { examId: string; error: string }[] }> {
  const exams = await db.exam.findMany({ where: { gridId }, select: { id: true, examId: true, rawText: true }, orderBy: { startedUtc: "asc" } });
  const failed: { examId: string; error: string }[] = [];
  let reanalyzed = 0;
  for (const e of exams) {
    try {
      const { report, normativeSetId } = await analyzeWithActiveNormative(e.rawText, gridId);
      await db.exam.update({ where: { id: e.id }, data: analysisColumns(report, normativeSetId) });
      reanalyzed++;
    } catch (err) {
      failed.push({ examId: e.examId, error: err instanceof Error ? err.message : String(err) });
    }
  }
  return { total: exams.length, reanalyzed, failed };
}

/** Portuguese label of a /core exclusion reason (CohortFilter.Reasons). */
export const exclusionReasonName: Record<string, string> = {
  gridMismatch: "número de pontos não corresponde à grade",
  aborted: "exame interrompido",
  strategy: "estratégia não mede limiar",
  age: "idade fora da faixa",
  fixationLosses: "perdas de fixação acima do limite",
  falsePositives: "falsos positivos acima do limite",
  falseNegatives: "falsos negativos acima do limite",
  missingData: "limiar ausente em ponto analisado",
  learningEffect: "primeiro exame do olho (efeito de aprendizado)",
  duplicateEye: "repetição do olho (mantido o mais recente)",
};
