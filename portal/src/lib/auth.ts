import { createHash, randomBytes } from "node:crypto";
import { cookies, headers } from "next/headers";
import { redirect } from "next/navigation";
import type { Role, User } from "@prisma/client";
import { db } from "./db.ts";
import { env } from "./env.ts";
import { hashPassword, verifyPassword } from "./password.ts";
import { afterFailure, afterSuccess, isLocked, sessionExpired } from "./security-policy.ts";

// Session cookie auth for clinic staff. The cookie holds a random token; the database stores only its
// SHA-256, so a database leak does not yield usable sessions. Phase 6: per-account lockout (User.failedLogins /
// lockedUntil), session rotation on login, idle expiry (Session.lastSeenAt), logout everywhere on password change.

export const SESSION_COOKIE = "qp_session";
const COOKIE = SESSION_COOKIE;
const sha256 = (s: string) => createHash("sha256").update(s).digest("hex");
const DUMMY_HASH = "scrypt$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

export type LoginResult = { user: User } | { error: "invalid" } | { error: "locked"; until: Date };

export async function login(email: string, password: string): Promise<LoginResult> {
  const now = new Date();
  const user = await db.user.findUnique({ where: { email: email.trim().toLowerCase() } });
  if (user && isLocked(user, now)) return { error: "locked", until: user.lockedUntil! };
  // Always run scrypt so response time does not reveal whether the e-mail exists.
  const ok = await verifyPassword(password, user?.passwordHash ?? DUMMY_HASH);
  if (!user || !user.active || !ok) {
    if (user) {
      const next = afterFailure(user, now);
      await db.user.update({ where: { id: user.id }, data: next });
      if (next.lockedUntil) return { error: "locked", until: next.lockedUntil };
    }
    return { error: "invalid" };
  }
  await db.user.update({ where: { id: user.id }, data: afterSuccess() });
  await issueSession(user.id);
  return { user };
}

/** Creates a fresh session and cookie; any session the browser already held is dropped (rotation on login). */
async function issueSession(userId: string): Promise<void> {
  const jar = await cookies();
  const old = jar.get(COOKIE)?.value;
  if (old) await db.session.deleteMany({ where: { id: sha256(old) } });
  const token = randomBytes(32).toString("base64url");
  const now = Date.now();
  await db.session.create({
    data: { id: sha256(token), userId, expiresAt: new Date(now + env.sessionTtlHours * 3600_000), lastSeenAt: new Date(now) },
  });
  jar.set(COOKIE, token, {
    httpOnly: true, sameSite: "lax", secure: env.secureCookies, path: "/", maxAge: env.sessionTtlHours * 3600,
  });
}

export async function logout(): Promise<void> {
  const jar = await cookies();
  const token = jar.get(COOKIE)?.value;
  if (token) await db.session.deleteMany({ where: { id: sha256(token) } });
  jar.delete(COOKIE);
}

export async function currentUser(): Promise<User | null> {
  const token = (await cookies()).get(COOKIE)?.value;
  if (!token) return null;
  const s = await db.session.findUnique({ where: { id: sha256(token) }, include: { user: true } });
  if (!s || !s.user.active) return null;
  const now = new Date();
  if (sessionExpired(s, env.sessionIdleMinutes, now)) {
    await db.session.deleteMany({ where: { id: s.id } });
    return null;
  }
  if (now.getTime() - s.lastSeenAt.getTime() > 60_000) {
    await db.session.update({ where: { id: s.id }, data: { lastSeenAt: now } }).catch(() => undefined);
  }
  return s.user;
}

/** Use at the top of every page, route and server action that touches patient data. */
export async function requireUser(roles?: Role[]): Promise<User> {
  const u = await currentUser();
  if (!u) redirect("/login");
  if (roles && !roles.includes(u.role)) redirect("/?forbidden=1");
  return u;
}

/**
 * Changes the caller's password after checking the current one; every other session of the user is ended
 * (logout everywhere) and the current one is rotated.
 */
export async function changePassword(user: User, current: string, next: string): Promise<string | null> {
  if (!(await verifyPassword(current, user.passwordHash))) return "A senha atual não confere.";
  let hash: string;
  try { hash = await hashPassword(next); } catch (e) { return e instanceof Error ? e.message : "Senha inválida."; }
  await db.user.update({ where: { id: user.id }, data: { passwordHash: hash, passwordChangedAt: new Date(), failedLogins: 0, lockedUntil: null } });
  await db.session.deleteMany({ where: { userId: user.id } });
  await issueSession(user.id);
  return null;
}

export async function clientIp(): Promise<string | null> {
  const h = await headers();
  return h.get("x-forwarded-for")?.split(",")[0]?.trim() ?? h.get("x-real-ip") ?? null;
}
