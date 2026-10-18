import { describe, expect, it } from "vitest";
import { codeHash, newPairingCode, newToken, normaliseCode } from "@/lib/headset-auth";
import { isLiveSnapshot } from "@/lib/live-types";

describe("pairing codes and tokens", () => {
  it("generates XXXX-XXXX codes without ambiguous letters", () => {
    for (let i = 0; i < 50; i++) expect(newPairingCode()).toMatch(/^[2-9A-HJKMNP-Z]{4}-[2-9A-HJKMNP-Z]{4}$/);
  });
  it("normalises hyphens, spaces and case; rejects other shapes", () => {
    expect(normaliseCode("7k3m-9q2x")).toBe("7K3M9Q2X");
    expect(normaliseCode(" 7K3M 9Q2X ")).toBe("7K3M9Q2X");
    expect(normaliseCode("7K3M-9Q2")).toBe("");
    expect(normaliseCode("0OIL-1234")).toBe("");
  });
  it("hashes the normalised code", () => {
    expect(codeHash("7k3m-9q2x")).toBe(codeHash("7K3M9Q2X"));
    expect(codeHash("7K3M9Q2X")).not.toBe(codeHash("7K3M9Q2Y"));
  });
  it("tokens are 32 random bytes and stored only as a hash", () => {
    const { token, id } = newToken();
    expect(Buffer.from(token, "base64url").length).toBe(32);
    expect(id).toMatch(/^[0-9a-f]{64}$/);
    expect(id).not.toContain(token);
  });
});

describe("live snapshot validation", () => {
  const ok = { schema: "perimetry-sync/1", sessionId: "ab12cd34ef56", seq: 1, sentUtc: "2026-10-18T17:00:00Z", state: "Exam", paused: false, finished: false, outcome: null,
    patientRef: "P-7K3M9Q", orderId: null, presetId: "24-2-zest", ageYears: 63, eye: "OD", startedUtc: "2026-10-18T16:55:00Z", elapsedMs: 1000, exam: null };
  it("accepts the documented shape", () => {
    expect(isLiveSnapshot(ok)).toBe(true);
    expect(isLiveSnapshot({ ...ok, exam: { locations: [] } })).toBe(true);
  });
  it("rejects missing or odd fields", () => {
    expect(isLiveSnapshot(null)).toBe(false);
    expect(isLiveSnapshot({ ...ok, sessionId: "../x" })).toBe(false);
    expect(isLiveSnapshot({ ...ok, seq: "1" })).toBe(false);
    expect(isLiveSnapshot({ ...ok, exam: { foo: 1 } })).toBe(false);
  });
});
