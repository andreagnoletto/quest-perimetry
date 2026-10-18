// Pure policy functions (Phase 6 security hardening), kept free of database and framework code so they are unit
// tested directly. The defaults were proposed to the product owner on 2026-10-18.

/** Account lockout: 10 consecutive failures within the window lock the account for the window. */
export const LOCKOUT = { maxFailures: 10, windowMs: 15 * 60_000 };

export interface LockState { failedLogins: number; lockedUntil: Date | null }

/** True while the account is locked. */
export function isLocked(u: LockState, now = new Date()): boolean {
  return !!u.lockedUntil && u.lockedUntil > now;
}

/** State after one more failed login. */
export function afterFailure(u: LockState, now = new Date()): LockState {
  const failed = u.failedLogins + 1;
  if (failed >= LOCKOUT.maxFailures) return { failedLogins: failed, lockedUntil: new Date(now.getTime() + LOCKOUT.windowMs) };
  return { failedLogins: failed, lockedUntil: u.lockedUntil && u.lockedUntil > now ? u.lockedUntil : null };
}

/** State after a successful login. */
export const afterSuccess = (): LockState => ({ failedLogins: 0, lockedUntil: null });

/** A session is over when its absolute expiry passed or it sat idle longer than `idleMinutes`. */
export function sessionExpired(s: { expiresAt: Date; lastSeenAt: Date }, idleMinutes: number, now = new Date()): boolean {
  if (s.expiresAt <= now) return true;
  return now.getTime() - s.lastSeenAt.getTime() > idleMinutes * 60_000;
}

/** Password policy for new and changed passwords (existing hashes are not re-checked). */
export const MIN_PASSWORD_LENGTH = 12;

export function passwordProblem(pw: string): string | null {
  if (pw.length < MIN_PASSWORD_LENGTH) return `A senha precisa ter pelo menos ${MIN_PASSWORD_LENGTH} caracteres`;
  if (pw.length > 200) return "A senha é longa demais";
  if (/^(.)\1+$/.test(pw)) return "A senha não pode repetir um único caractere";
  // a dictionary word padded with digits/punctuation ("senha2026!!", "password-12345678") or a keyboard run
  const letters = pw.toLowerCase().replace(/[^a-z]/g, "");
  if (["", "senha", "password", "qwertyuiop", "abcdefghijkl", "admin", "perimetria"].includes(letters)) return "A senha é previsível demais";
  if (/^(0123456789|1234567890)/.test(pw)) return "A senha é previsível demais";
  return null;
}
