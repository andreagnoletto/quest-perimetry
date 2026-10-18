import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { env } from "@/lib/env";
import { analyzeExam } from "@/lib/perimetry";
import { examSummary } from "@/lib/importer";
import { examOrderFile } from "@/lib/headset";
import { fixationLine, fixationTargetLine, fovealLine, gazeBar, gazeCaption } from "@/lib/report-format";
import { ditherPath, tickStep } from "@/components/FieldMaps";
import type { Report } from "@/lib/report-types";

// Phase 5: foveal threshold, gaze trace, GHT applicability, 10-2 layout, order options. Exams come from the
// real /core simulator so the portal is tested against the report it will actually receive.

let dir: string;
let foveal: Report; // 24-2 with foveal test and simulated eye tracking
let macular: Report; // 10-2 (no GHT)
let plain: Report; // 24-2 defaults

function simulate(name: string, args: string[]): string {
  const [cmd, ...pre] = env.cli;
  const out = path.join(dir, name);
  execFileSync(cmd, [...pre, "simulate", "--strategy", "ZestFast", "--config", env.configDir, "--out", out, ...args]);
  return readFileSync(out, "utf8");
}

beforeAll(async () => {
  dir = mkdtempSync(path.join(tmpdir(), "qp-p5-"));
  foveal = await analyzeExam(simulate("f.json", ["--defect", "SuperiorArcuate", "--seed", "4", "--age", "55", "--foveal", "true", "--gaze", "1.5", "--fixation", "CentralEyeTracking"]));
  macular = await analyzeExam(simulate("m.json", ["--grid", "10-2", "--defect", "CentralScotoma", "--seed", "5", "--age", "60"]));
  plain = await analyzeExam(simulate("p.json", ["--defect", "Normal", "--seed", "8", "--age", "40"]));
}, 120_000);

afterAll(() => rmSync(dir, { recursive: true, force: true }));

describe("foveal threshold", () => {
  it("prints the HFA-equivalent value with its p-mark when tested, and 'não testado' otherwise", () => {
    const f = fovealLine(foveal, 16);
    expect(f.tested).toBe(true);
    expect(f.value).toMatch(/^\d+ dB$/);
    expect(Number(f.value.split(" ")[0])).toBe(Math.round(foveal.exam.Foveal!.ThresholdHfaDb));
    expect(f.p === "" || /^p < /.test(f.p)).toBe(true);
    expect(fovealLine(plain, 16)).toEqual({ value: "não testado", p: "", tested: false });
    expect(fixationTargetLine(foveal)).toBe("Central + losango de 4 pontos na fóvea");
    expect(fixationTargetLine(plain)).toBe("Central");
  });
  it("prints '<floor' when censored at the device floor", () => {
    const r: Report = { ...foveal, exam: { ...foveal.exam, Foveal: { ...foveal.exam.Foveal!, CensoredLow: true } } };
    expect(fovealLine(r, 16).value).toBe("<16 dB");
  });
  it("is copied to the exam row for the lists, together with the gaze flag", () => {
    expect(examSummary(foveal)).toMatchObject({ fovealHfaDb: foveal.exam.Foveal!.ThresholdHfaDb, gazeTracked: true, ght: "OutsideNormalLimits" });
    expect(examSummary(plain)).toMatchObject({ fovealHfaDb: null, gazeTracked: false });
    expect(examSummary(macular)).toMatchObject({ fovealHfaDb: null, gazeTracked: false, ght: "NotApplicable" });
  });
});

describe("gaze trace", () => {
  it("builds one bar per traced presentation, clipped at 10°, with rejected ones marked", () => {
    const g = gazeBar(foveal)!;
    const trace = foveal.exam.Reliability.GazeTrace!;
    expect(g.bars.length).toBe(trace.length);
    expect(g.bars.every((b) => b.h >= 0 && b.h <= 1)).toBe(true);
    expect(g.bars.filter((b) => b.rejected).length).toBe(trace.filter((t) => t.Rejected).length);
    expect(g.rejected).toBe(foveal.exam.Reliability.GazeRejectedPresentations);
    expect(g.limit).toBeCloseTo(0.3, 6); // 3° of 10°
    expect(gazeCaption(g)).toMatch(/^Rastreamento do olhar: erro médio \d+,\d°, \d+ descartadas? \(\d+ apresentações rastreadas\)$/);
    expect(fixationLine(foveal)).toBe("Eye-tracking");
  });
  it("is absent without eye tracking", () => {
    expect(gazeBar(plain)).toBeNull();
    expect(fixationLine(plain)).toBe("Heijl-Krakau (mancha cega)");
  });
  it("names forced choice and the oculokinetic mode", () => {
    const r: Report = { ...plain, exam: { ...plain.exam, Settings: { ...plain.exam.Settings, FixationMode: "Oculokinetic", ResponseMode: "ForcedChoice" } } };
    expect(fixationLine(r)).toBe("Oculocinética (modo B) · Escolha forçada (experimental)");
    expect(fixationTargetLine(r)).toBe("Móvel (oculocinético)");
  });
});

describe("grids", () => {
  it("has no GHT on the 10-2 and keeps it on the 24-2", () => {
    expect(macular.analysis!.GhtApplicable).toBe(false);
    expect(macular.analysis!.Ght).toBe("NotApplicable");
    expect(macular.text!.ght).toBe("Não se aplica");
    expect(macular.analysis!.Points.length).toBe(68);
    expect(plain.analysis!.GhtApplicable).toBe(true);
  });
  it("uses 5° ticks on the macular frame and 10° elsewhere, and a lattice that fits 2° cells", () => {
    expect(tickStep(12)).toBe(5);
    expect(tickStep(28)).toBe(10);
    expect(tickStep(32)).toBe(10);
    // 2° cells: 0.1° lattice, black cell covers the whole area
    const area = (d: string, q: number) => [...d.matchAll(/h([\d.]+)v/g)].reduce((s, m) => s + Number(m[1]) * q, 0);
    expect(area(ditherPath(-1, -1, 2, 0), 0.1)).toBeCloseTo(4, 6);
    expect(area(ditherPath(-3, -3, 6, 0), 0.3)).toBeCloseTo(36, 6); // unchanged for the 6° grids
  });
});

describe("exam order options", () => {
  it("writes the Phase 5 keys only when set", () => {
    const base = { code: "P-ABC234", ageYears: 63.4, refraction: null, presetId: "24-2-zest" };
    expect(examOrderFile(base)).toEqual({ patientRef: "P-ABC234", ageYears: 63.4, refractiveCorrection: null, presetId: "24-2-zest" });
    expect(examOrderFile({ ...base, gridId: "10-2", fixationMode: "Oculokinetic", fovealThreshold: true, responseMode: "ForcedChoice", seed: "7" }))
      .toEqual({ patientRef: "P-ABC234", ageYears: 63.4, refractiveCorrection: null, presetId: "24-2-zest", seed: 7, grid: "10-2", fixationMode: "Oculokinetic", fovealThreshold: true, responseMode: "ForcedChoice" });
    expect(examOrderFile({ ...base, responseMode: "YesNo", fovealThreshold: false, gridId: null })).not.toHaveProperty("responseMode");
  });
});

import { deviceLine } from "../../src/components/SingleFieldReport.tsx";

describe("deviceLine", () => {
  it("prints model and serial, falls back to label, empty without a device", () => {
    expect(deviceLine({ model: "Meta Quest 3", serial: "ABC123", label: null })).toBe(" · aparelho Meta Quest 3 (S/N ABC123)");
    expect(deviceLine({ model: null, serial: "X1", label: "Sala 2" })).toBe(" · aparelho Sala 2 (S/N X1)");
    expect(deviceLine(null)).toBe("");
  });
});
