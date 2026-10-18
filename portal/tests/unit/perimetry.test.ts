import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, rmSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { afterAll, beforeAll, describe, expect, it } from "vitest";
import { env } from "@/lib/env";
import { analyzeExam, checkCalibration, parseHeadsetJson } from "@/lib/perimetry";
import { pointsCsv } from "@/lib/exams";
import { examOrderFile } from "@/lib/headset";
import { ditherPath, probLevel } from "@/components/FieldMaps";

// Integration with the real /core CLI (built with `dotnet build core/Perimetry.Cli -c Release`).

let dir: string;
let examText: string;

beforeAll(() => {
  dir = mkdtempSync(path.join(tmpdir(), "qp-test-"));
  const [cmd, ...pre] = env.cli;
  const out = path.join(dir, "exam.json");
  execFileSync(cmd, [...pre, "simulate", "--strategy", "ZestFast", "--defect", "SuperiorArcuate", "--seed", "4", "--patient-ref", "P-TEST01", "--config", env.configDir, "--out", out]);
  examText = readFileSync(out, "utf8");
}, 60_000);

afterAll(() => rmSync(dir, { recursive: true, force: true }));

describe("perimetry CLI bridge", () => {
  it("analyses a headset exam into a strict report", async () => {
    const r = await analyzeExam(examText);
    expect(r.schema).toBe("perimetry-report/1");
    expect(r.exam.Settings.PatientRef).toBe("P-TEST01");
    expect(r.analysis).not.toBeNull();
    const a = r.analysis!;
    expect(a.Points.length).toBe(54);
    expect(a.Md).toBeLessThan(-2); // superior arcuate defect
    expect(r.text?.ght).toBeTruthy();
    expect(r.warnings.join(" ")).toMatch(/PROVIS/);
    expect(r.exam.Disclaimer).toMatch(/não registrado na ANVISA/);
    // one CSV row per location
    expect(pointsCsv(r).trim().split("\n").length).toBe(55);
  }, 30_000);

  it("reports invalid files as errors", async () => {
    await expect(analyzeExam('{"hello":1}')).rejects.toThrow(/invalid exam file|not an exam/);
  }, 30_000);

  it("validates calibration files", async () => {
    const good = {
      DeviceSerial: "Q3-1", Photometer: "Konica LS-160", MeasuredAtUtc: "2026-10-01T10:00:00Z", ValidUntilUtc: "2027-04-01T00:00:00Z", IsMeasured: true,
      Eyes: { OD: { Samples: [{ Level: 0, Cdm2: 0.01 }, { Level: 0.5, Cdm2: 25 }, { Level: 1, Cdm2: 98 }] }, OS: { Samples: [{ Level: 0, Cdm2: 0.02 }, { Level: 1, Cdm2: 101 }] } },
    };
    const c = await checkCalibration(JSON.stringify(good));
    expect(c.ok).toBe(true);
    expect(c.DeviceSerial).toBe("Q3-1");
    expect(c.eyes?.OD.maxCdm2).toBe(98);
    expect(c.missingEyes ?? []).toEqual([]);
    const bad = { ...good, Eyes: { OD: { Samples: [{ Level: 0.5, Cdm2: 1 }, { Level: 0.2, Cdm2: 2 }] } } };
    expect((await checkCalibration(JSON.stringify(bad))).ok).toBe(false);
  }, 30_000);
});

describe("helpers", () => {
  it("parses legacy NaN values as null", () => {
    expect(parseHeadsetJson('{"a":NaN,"b":[1,-Infinity],"c":"NaN"}')).toEqual({ a: null, b: [1, null], c: "NaN" });
  });

  it("writes the exam order the headset reads, without identifying data", () => {
    expect(examOrderFile({ code: "P-ABC234", ageYears: 63.456, refraction: "+2,00 esf", presetId: "24-2-zest", seed: "42" }))
      .toEqual({ patientRef: "P-ABC234", ageYears: 63.5, refractiveCorrection: "+2,00 esf", presetId: "24-2-zest", seed: 42 });
  });

  it("maps /core probabilities to HFA symbols", () => {
    expect([0.005, 0.01, 0.02, 0.05, 1, null].map(probLevel)).toEqual(["p05", "p1", "p2", "p5", "ns", null]);
  });
});

describe("grayscale dither", () => {
  const area = (d: string) => [...d.matchAll(/h([\d.]+)v/g)].reduce((s, m) => s + Number(m[1]) * 0.3, 0);
  it("covers the cell in proportion to the band, on either side of fixation", () => {
    for (const [x, y] of [[-6, -6], [0, 0], [-9, 3]]) {
      expect(area(ditherPath(x, y, 6, 0))).toBeCloseTo(36, 6); // black
      expect(ditherPath(x, y, 6, 9)).toBe(""); // white
      const mid = area(ditherPath(x, y, 6, 5)) / 36;
      expect(mid).toBeGreaterThan(0.3);
      expect(mid).toBeLessThan(0.6);
    }
  });
});
