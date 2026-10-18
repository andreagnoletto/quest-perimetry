import { describe, expect, it } from "vitest";
import { afterFailure, afterSuccess, isLocked, LOCKOUT, MIN_PASSWORD_LENGTH, passwordProblem, sessionExpired } from "@/lib/security-policy";

const t0 = new Date("2026-10-18T12:00:00Z");
const min = (n: number) => new Date(t0.getTime() + n * 60_000);

describe("account lockout", () => {
  it("locks on the 10th consecutive failure for 15 minutes", () => {
    let s = { failedLogins: 0, lockedUntil: null as Date | null };
    for (let i = 1; i < LOCKOUT.maxFailures; i++) {
      s = afterFailure(s, t0);
      expect(s.failedLogins).toBe(i);
      expect(isLocked(s, t0)).toBe(false);
    }
    s = afterFailure(s, t0);
    expect(s.failedLogins).toBe(10);
    expect(isLocked(s, t0)).toBe(true);
    expect(isLocked(s, min(14))).toBe(true);
    expect(isLocked(s, min(15))).toBe(false);
  });
  it("a successful login clears the counter and the lock", () => {
    expect(afterSuccess()).toEqual({ failedLogins: 0, lockedUntil: null });
  });
  it("keeps a running lock while more failures arrive", () => {
    const locked = afterFailure({ failedLogins: 9, lockedUntil: null }, t0);
    const again = afterFailure(locked, min(1));
    expect(again.lockedUntil!.getTime()).toBeGreaterThanOrEqual(locked.lockedUntil!.getTime());
    expect(isLocked(again, min(14))).toBe(true);
  });
});

describe("session expiry", () => {
  const s = { expiresAt: min(12 * 60), lastSeenAt: t0 };
  it("expires after the idle timeout even before the absolute expiry", () => {
    expect(sessionExpired(s, 60, min(59))).toBe(false);
    expect(sessionExpired(s, 60, min(61))).toBe(true);
  });
  it("expires at the absolute expiry", () => {
    expect(sessionExpired({ expiresAt: min(10), lastSeenAt: min(9) }, 60, min(10))).toBe(true);
  });
});

describe("password policy", () => {
  it("requires 12 characters and rejects trivial patterns", () => {
    expect(MIN_PASSWORD_LENGTH).toBe(12);
    expect(passwordProblem("curta123")).toMatch(/12 caracteres/);
    expect(passwordProblem("aaaaaaaaaaaaaaaa")).toMatch(/repetir/);
    expect(passwordProblem("senha-2026-!!!!")).toMatch(/previsível/);
    expect(passwordProblem("password12345")).toMatch(/previsível/);
    expect(passwordProblem("qwertyuiop123456")).toMatch(/previsível/);
    expect(passwordProblem("012345678901")).toMatch(/previsível/);
    expect(passwordProblem("123456789012")).toMatch(/previsível/);
    expect(passwordProblem("x".repeat(201))).toMatch(/longa/);
  });
  it("accepts an ordinary passphrase, including one that merely starts with a common word", () => {
    expect(passwordProblem("correct horse battery")).toBeNull();
    expect(passwordProblem("senha-de-teste-123")).toBeNull(); // the e2e user
  });
});
