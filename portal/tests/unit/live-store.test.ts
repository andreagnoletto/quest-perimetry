import { describe, expect, it } from "vitest";
import { cached, isActive, publish, subscribe, subscribeList } from "@/lib/live";
import type { LiveView } from "@/lib/live-types";

const view = (sessionId: string, seq: number): LiveView => ({
  sessionId, receivedUtc: new Date().toISOString(), deviceLabel: "Quest 3", pendingCommands: [],
  snapshot: { schema: "perimetry-sync/1", sessionId, seq, sentUtc: "", state: "Exam", paused: false, finished: false, outcome: null, patientRef: null, orderId: null, presetId: null, ageYears: null, eye: "OD", startedUtc: "2026-10-18T16:55:00Z", elapsedMs: 0, exam: null },
});

describe("in-process live store", () => {
  it("delivers each published snapshot to the session's subscribers only", () => {
    const got: number[] = [], other: number[] = [];
    const off = subscribe("s1", (v) => got.push(v.snapshot.seq));
    const off2 = subscribe("s2", (v) => other.push(v.snapshot.seq));
    publish(view("s1", 1)); publish(view("s1", 2));
    expect(got).toEqual([1, 2]);
    expect(other).toEqual([]);
    expect(cached("s1")?.snapshot.seq).toBe(2);
    off(); off2();
    publish(view("s1", 3));
    expect(got).toEqual([1, 2]);
  });
  it("notifies list subscribers and survives a throwing listener", () => {
    let n = 0;
    const off = subscribeList(() => { n++; });
    const bad = subscribe("s3", () => { throw new Error("gone"); });
    publish(view("s3", 1));
    expect(n).toBe(1);
    off(); bad();
  });
  it("activity window is 30 s", () => {
    const now = Date.now();
    expect(isActive(new Date(now - 10_000), now)).toBe(true);
    expect(isActive(new Date(now - 31_000), now)).toBe(false);
  });
});
