import type { Exam } from "@prisma/client";
import { audit } from "./audit.ts";
import { db } from "./db.ts";
import { view } from "./patients.ts";
import { trend, type Trend } from "./progression.ts";
import { normativeLine } from "./report-format.ts";
import type { Eye, Report } from "./report-types.ts";

// Phase 6: the per-eye progression overview (HFA "Overview" printout + the MD / VFI trend). The portal only
// orders the exams in time and lays the /core numbers out; the trend is the descriptive OLS of
// lib/progression.ts (SPEC §7), not an event analysis (GPA).

export interface OverviewExam { exam: Exam; report: Report }

export interface OverviewGrid {
  gridId: string;
  exams: OverviewExam[];
  md: Trend;
  vfi: Trend;
  mdPoints: { date: Date; value: number | null }[];
  vfiPoints: { date: Date; value: number | null }[];
}

export interface Overview {
  patient: { id: string; name: string; internalId: string; birthDate: string; code: string };
  eye: Eye;
  /** Threshold exams (no screening, no interrupted exam) grouped by grid, most recently used grid first. */
  grids: OverviewGrid[];
  /** Exams left out of the series and why (listed in the footer so nothing silently disappears). */
  omitted: { exam: Exam; reason: string }[];
  /** Distinct normative lines and analysis config hashes of the exams shown. */
  normatives: string[];
  configs: string[];
}

/** Groups one eye's exams for the overview. Pure, so the ordering rules can be unit-tested. */
export function groupForOverview(exams: Exam[]): { grids: OverviewGrid[]; omitted: Overview["omitted"] } {
  const omitted: Overview["omitted"] = [];
  const byGrid = new Map<string, OverviewExam[]>();
  for (const exam of [...exams].sort((a, b) => a.startedUtc.getTime() - b.startedUtc.getTime())) {
    const report = exam.report as unknown as Report;
    if (exam.aborted || !report.analysis) { omitted.push({ exam, reason: "interrompido" }); continue; }
    if (exam.strategy === "Screening") { omitted.push({ exam, reason: "rastreio (sem limiares)" }); continue; }
    const list = byGrid.get(exam.gridId) ?? [];
    list.push({ exam, report });
    byGrid.set(exam.gridId, list);
  }
  const grids = [...byGrid.entries()]
    .sort((a, b) => b[1][b[1].length - 1].exam.startedUtc.getTime() - a[1][a[1].length - 1].exam.startedUtc.getTime())
    .map(([gridId, list]) => {
      const mdPoints = list.map((x) => ({ date: x.exam.startedUtc, value: x.exam.md }));
      const vfiPoints = list.map((x) => ({ date: x.exam.startedUtc, value: x.exam.vfi }));
      return { gridId, exams: list, mdPoints, vfiPoints, md: trend(mdPoints), vfi: trend(vfiPoints) };
    });
  return { grids, omitted };
}

export async function loadOverview(patientId: string, eye: Eye, userId: string, action: string): Promise<Overview | null> {
  const row = await db.patient.findUnique({ where: { id: patientId }, include: { exams: { where: { eye }, orderBy: { startedUtc: "asc" } } } });
  if (!row) return null;
  await audit(userId, action, "patient", patientId, `${eye}; ${row.exams.length} exames`);
  const p = view(row);
  const { grids, omitted } = groupForOverview(row.exams);
  const shown = grids.flatMap((g) => g.exams);
  return {
    patient: { id: p.id, name: p.name, internalId: p.internalId, birthDate: p.birthDate, code: p.code },
    eye,
    grids,
    omitted,
    normatives: [...new Set(shown.map((x) => normativeLine(x.report)).filter(Boolean))],
    configs: [...new Set(shown.map((x) => x.report.analyzedWith.configHash.slice(0, 12)))],
  };
}

export function parseEye(v: string | undefined): Eye {
  return v === "OS" ? "OS" : "OD";
}
