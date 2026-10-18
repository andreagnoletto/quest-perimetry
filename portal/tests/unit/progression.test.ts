import { describe, expect, it } from "vitest";
import { studentTCdf, trend } from "@/lib/progression";

const d = (years: number) => new Date(Date.UTC(2020, 0, 1) + years * 365.2425 * 86400_000);

describe("trend", () => {
  it("recovers an exact slope", () => {
    const t = trend([0, 1, 2, 3].map((y) => ({ date: d(y), value: -1 - 0.5 * y })));
    expect(t.slopePerYear).toBeCloseTo(-0.5, 9);
    expect(t.intercept).toBeCloseTo(-1, 9);
    expect(t.spanYears).toBeCloseTo(3, 6);
    expect(t.p).toBe(0);
  });

  it("matches a textbook OLS p-value", () => {
    // y = 1, 3, 2, 5, 4 at x = 0..4: slope 0.8, SE √0.12 = 0.3464, t = 2.309, df 3 → p = 0.1041 (closed-form t CDF for 3 df)
    const t = trend([1, 3, 2, 5, 4].map((v, i) => ({ date: d(i), value: v })));
    expect(t.slopePerYear).toBeCloseTo(0.8, 6);
    expect(t.slopeSe).toBeCloseTo(0.3464, 4);
    expect(t.p).toBeCloseTo(0.10409, 4);
  });

  it("handles too few points and nulls", () => {
    expect(trend([{ date: d(0), value: 1 }, { date: d(1), value: null }]).slopePerYear).toBeNull();
    const two = trend([{ date: d(0), value: 1 }, { date: d(2), value: 0 }]);
    expect(two.slopePerYear).toBeCloseTo(-0.5, 9);
    expect(two.p).toBeNull();
  });

  it("t CDF is symmetric and correct at known points", () => {
    expect(studentTCdf(0, 5)).toBeCloseTo(0.5, 9);
    expect(studentTCdf(2.015, 5)).toBeCloseTo(0.95, 3);
    expect(studentTCdf(-2.015, 5)).toBeCloseTo(0.05, 3);
  });
});
