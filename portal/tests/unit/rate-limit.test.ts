import { describe, expect, it } from "vitest";
import { hit, LOGIN_IP_LIMIT, minutesLeft, PAIRING_IP_LIMIT, reset } from "@/lib/rate-limit";

describe("per-IP sliding window", () => {
  it("allows `limit` hits per window, then refuses with a retry delay", () => {
    reset("t:a");
    const t0 = 1_000_000;
    for (let i = 0; i < 3; i++) expect(hit("t:a", 3, 60_000, t0 + i).allowed).toBe(true);
    const r = hit("t:a", 3, 60_000, t0 + 10);
    expect(r.allowed).toBe(false);
    expect(r.remaining).toBe(0);
    expect(r.retryAfterMs).toBe(60_000 - 10);
  });
  it("forgets hits that left the window; keys are independent", () => {
    reset("t:b"); reset("t:c");
    const t0 = 5_000_000;
    for (let i = 0; i < 3; i++) hit("t:b", 3, 60_000, t0);
    expect(hit("t:b", 3, 60_000, t0 + 59_999).allowed).toBe(false);
    expect(hit("t:b", 3, 60_000, t0 + 60_000).allowed).toBe(true);
    expect(hit("t:c", 3, 60_000, t0).allowed).toBe(true);
  });
  it("documents the login and pairing ceilings", () => {
    expect(LOGIN_IP_LIMIT).toEqual({ limit: 50, windowMs: 15 * 60_000 });
    expect(PAIRING_IP_LIMIT).toEqual({ limit: 20, windowMs: 15 * 60_000 });
    expect(minutesLeft(1)).toBe(1);
    expect(minutesLeft(61_000)).toBe(2);
  });
});
