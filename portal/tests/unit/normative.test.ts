import { execFile } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { promisify } from "node:util";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { env } from "@/lib/env";
import { analyzeExam, normativeBuild, normativeStats } from "@/lib/perimetry";
import { analysisColumns, examSummary } from "@/lib/importer";
import { cohortFromPatients, studyId, type CohortPatient } from "@/lib/normative";
import { groupForOverview } from "@/lib/overview";
import { normativeLine } from "@/lib/report-format";
import type { Report } from "@/lib/report-types";

// Phase 6: the portal side of the clinic's own normative (cohort file, bridge to `perimetry normative`, report
// footer, re-analysis columns) and the ordering rules of the progression overview. Exams come from the real /core
// simulator; the rules themselves (exclusions, status) are /core's and are only observed here.

process.env.PERIMETRY_DATA_KEY ??= Buffer.alloc(32, 7).toString("base64");

const run = promisify(execFile);
let dir: string;
const GRID_POINTS = 54; // 24-2

async function simulate(name: string, args: string[]): Promise<string> {
  const [cmd, ...pre] = env.cli;
  const out = path.join(dir, name);
  await run(cmd, [...pre, "simulate", "--strategy", "ZestFast", "--config", env.configDir, "--out", out, ...args]);
  return readFileSync(out, "utf8");
}

/** 16 healthy subjects × 2 eyes, ages 20–80: enough for /core to build (≥ 30 eyes) but not for MEASURED (100). */
const SUBJECTS = Array.from({ length: 16 }, (_, i) => ({ id: `patient-${i}`, age: 20 + i * 4, birth: `${2026 - (20 + i * 4)}-03-15` }));
let cohortPatients: CohortPatient[];
let plain: Report; // one 24-2 of a patient outside the cohort (age 56)
let plainRaw: string;

function examRow(report: Report, id: string, startedUtc: Date, aborted = false) {
  return { id, examId: report.exam.ExamId, eye: report.exam.Settings.Eye, gridId: report.exam.Settings.GridId, strategy: report.exam.Settings.Strategy, aborted, startedUtc, report };
}

beforeAll(async () => {
  dir = mkdtempSync(path.join(tmpdir(), "qp-p6-"));
  const reports: Report[][] = [];
  // four simulations at a time: each is a dotnet process of ~1 s
  for (let i = 0; i < SUBJECTS.length; i += 4) {
    const batch = await Promise.all(SUBJECTS.slice(i, i + 4).map(async (s, k) => {
      const seed = 100 + (i + k) * 2;
      // reliable observers: with 1 catch trial in 30, a single false positive already exceeds the 15% limit /core applies
      const reliable = ["--fpr", "0", "--fnr", "0", "--fixloss", "0"];
      const od = await analyzeExam(await simulate(`s${i + k}-od.json`, ["--defect", "Normal", "--seed", String(seed), "--age", String(s.age), ...reliable]));
      const os = await analyzeExam(await simulate(`s${i + k}-os.json`, ["--defect", "Normal", "--eye", "OS", "--seed", String(seed + 1), "--age", String(s.age), ...reliable]));
      return [od, os];
    }));
    reports.push(...batch);
  }
  cohortPatients = SUBJECTS.map((s, i) => ({ id: s.id, birthDate: s.birth, exams: reports[i].map((r, k) => examRow(r, `${s.id}-${k}`, new Date("2026-06-01T10:00:00Z"))) }));
  plainRaw = await simulate("plain.json", ["--defect", "SuperiorArcuate", "--seed", "4", "--age", "56"]);
  plain = await analyzeExam(plainRaw);
}, 300_000);

afterAll(() => rmSync(dir, { recursive: true, force: true }));

describe("cohort file", () => {
  it("numbers each eye's exams chronologically, copies HFA-eq thresholds with null at the blind spot, and is pseudonymous", () => {
    const [od, os] = cohortPatients[0].exams.map((e) => e.report as Report);
    const p: CohortPatient = {
      id: "patient-x", birthDate: "1980-01-01",
      exams: [
        examRow(od, "e3", new Date("2026-03-01T10:00:00Z")),
        examRow(od, "e1", new Date("2025-01-01T10:00:00Z")),
        examRow(os, "e2", new Date("2025-06-01T10:00:00Z")),
        examRow({ ...od, analysis: null }, "e4", new Date("2026-05-01T10:00:00Z"), true),
      ],
    };
    const file = cohortFromPatients([p], "24-2", GRID_POINTS, new Date("2026-10-18T00:00:00Z"));
    expect(file.schema).toBe("perimetry-cohort/1");
    expect(file.gridId).toBe("24-2");
    expect(file.subjects.map((s) => [s.examId, s.eye, s.sequenceForEye])).toEqual([
      [od.exam.ExamId, "OD", 1], [os.exam.ExamId, "OS", 1], [od.exam.ExamId, "OD", 2], [od.exam.ExamId, "OD", 3],
    ]);
    const first = file.subjects[0];
    expect(first.subjectId).toBe(studyId("patient-x"));
    expect(first.subjectId).toMatch(/^S-[0-9a-f]{12}$/);
    expect(JSON.stringify(file)).not.toContain("patient-x");
    expect(first.ageYears).toBe(45);
    expect(first.thresholdsHfaDb).toHaveLength(GRID_POINTS);
    od.analysis!.Points.forEach((pt, i) => expect(first.thresholdsHfaDb[i]).toBe(pt.IsBlindSpot ? null : pt.HfaEqDb));
    expect(first.thresholdsHfaDb.filter((v) => v === null)).toHaveLength(2);
    expect(first.reliability.fixationLossRate).toBe(od.exam.Reliability.FixationLossRate ?? 0);
    expect(first.fovealHfaDb).toBeNull();
    // the interrupted exam keeps the grid's size so /core reports "aborted", not a grid mismatch
    const aborted = file.subjects[3];
    expect(aborted.aborted).toBe(true);
    expect(aborted.thresholdsHfaDb).toHaveLength(GRID_POINTS);
    expect(aborted.thresholdsHfaDb.every((v) => v === null)).toBe(true);
    // exams of another grid never enter
    expect(cohortFromPatients([p], "10-2", 68).subjects).toHaveLength(0);
  });

  it("is accepted by `perimetry normative stats` and `build`, and the built database drives `report --normative`", async () => {
    const file = cohortFromPatients(cohortPatients, "24-2", GRID_POINTS);
    const json = JSON.stringify(file);
    const stats = await normativeStats(json, "24-2");
    expect(stats.schema).toBe("perimetry-normative-build/1");
    expect(stats.candidates).toBe(32);
    expect(stats.excluded).toEqual([]);
    expect(stats.included).toBe(32);
    expect(stats.subjects).toBe(16);
    expect(stats.eyesOd).toBe(16);
    expect(stats.status).toBe("PROVISIONAL");
    expect(stats.ageRange[0]).toBeCloseTo(20.2, 0);
    expect(stats.eyesByBand.map((b) => b.Eyes).reduce((a, b) => a + b, 0)).toBe(32);
    expect(stats.database).toBeNull();

    const { summary, databaseJson } = await normativeBuild(json, "24-2", "test-24-2");
    expect(summary.database?.id).toBe("test-24-2");
    expect(summary.database?.populationEyes).toBe(32);
    expect(summary.cohortHash).toBe(stats.cohortHash);
    const db = JSON.parse(databaseJson) as { schema: string; Id?: string; id?: string };
    expect(db.schema).toBe("perimetry-normative/1");

    const r = await analyzeExam(plainRaw, databaseJson);
    expect(r.analyzedWith.normative).toMatchObject({ id: "test-24-2", status: "PROVISIONAL", source: "cohort", populationEyes: 32 });
    expect(r.analysis?.NormativeSource).toBe("cohort");
    expect(r.warnings.some((w) => /NORMATIVO PRÓPRIO PROVISÓRIO — 32 olhos/.test(w))).toBe(true);
    expect(normativeLine(r)).toBe("normativo próprio test-24-2 (32 olhos, 20–80 anos, provisório)");
    // the derived columns of the exam row follow the re-analysis
    expect(analysisColumns(r, "set-1")).toMatchObject({ normativeSetId: "set-1", analyzedConfig: r.analyzedWith.configHash, ...examSummary(r) });
    expect(analysisColumns(r, "set-1").md).toBe(r.analysis!.Md);
  }, 120_000);
});

describe("report footer", () => {
  it("names the provisional synthetic normative when no clinic set was used, and nothing for an exam without analysis", () => {
    expect(normativeLine(plain)).toBe("normativo provisório sintético");
    expect(normativeLine({ ...plain, analysis: null })).toBe("");
    const measured: Report = { ...plain, analyzedWith: { ...plain.analyzedWith, normative: { id: "clinic-24-2-2027-01-10", status: "MEASURED", source: "cohort", populationEyes: 120, ageMin: 19.5, ageMax: 84.2 } } };
    expect(normativeLine(measured)).toBe("normativo próprio clinic-24-2-2027-01-10 (120 olhos, 20–84 anos)");
  });
});

describe("progression overview", () => {
  it("orders threshold exams by date per grid, newest grid first, and lists interrupted and screening exams as omitted", () => {
    const base = { id: "", examId: "", eye: "OD", patientId: null, sessionId: null, strategy: "ZestFast", gridId: "24-2", aborted: false, calibrated: false, md: null, psd: null, vfi: null, ght: null, fovealHfaDb: null, gazeTracked: false, integrity: "unverified", rawText: "", rawHmac: null, configHash: null, analyzedConfig: "", importedAt: new Date(), importedById: null, normativeSetId: null, report: plain as unknown as object };
    const mk = (id: string, date: string, extra: Partial<typeof base> = {}) => ({ ...base, id, examId: id, startedUtc: new Date(date), md: -2, vfi: 97, ...extra });
    const { grids, omitted } = groupForOverview([
      mk("c", "2026-01-01"), mk("a", "2024-01-01"), mk("b", "2025-01-01"),
      mk("s", "2025-06-01", { strategy: "Screening" }),
      mk("x", "2025-07-01", { aborted: true, report: { ...plain, analysis: null } as unknown as object }),
      mk("m", "2026-02-01", { gridId: "10-2" }),
    ] as Parameters<typeof groupForOverview>[0]);
    expect(grids.map((g) => g.gridId)).toEqual(["10-2", "24-2"]);
    expect(grids[1].exams.map((x) => x.exam.id)).toEqual(["a", "b", "c"]);
    expect(grids[1].md.n).toBe(3);
    expect(grids[1].md.slopePerYear).toBeCloseTo(0, 6);
    expect(omitted.map((o) => [o.exam.id, o.reason])).toEqual([["s", "rastreio (sem limiares)"], ["x", "interrompido"]]);
  });
});
