import { createHash, randomBytes } from "node:crypto";
import { cookies, headers } from "next/headers";
import { redirect } from "next/navigation";
import type { Role, User } from "@prisma/client";
import { db } from "./db.ts";
import { env } from "./env.ts";
import { verifyPassword } from "./password.ts";

// Session cookie auth for clinic staff. The cookie holds a random token; the database stores only its
// SHA-256, so a database leak does not yield usable sessions. Headset tokens come in Phase 4.

export const SESSION_COOKIE = "qp_session";
const COOKIE = SESSION_COOKIE;
const sha256 = (s: string) => createHash("sha256").update(s).digest("hex");

export async function login(email: string, password: string): Promise<User | null> {
  const user = await db.user.findUnique({ where: { email: email.trim().toLowerCase() } });
  // Always run scrypt so response time does not reveal whether the e-mail exists.
  const ok = await verifyPassword(password, user?.passwordHash ?? "scrypt$AAAAAAAAAAAAAAAAAAAAAA$AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");
  if (!user || !user.active || !ok) return null;
  const token = randomBytes(32).toString("base64url");
  await db.session.create({
    data: { id: sha256(token), userId: user.id, expiresAt: new Date(Date.now() + env.sessionTtlHours * 3600_000) },
  });
  (await cookies()).set(COOKIE, token, {
    httpOnly: true, sameSite: "lax", secure: env.secureCookies, path: "/", maxAge: env.sessionTtlHours * 3600,
  });
  return user;
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
  if (!s || s.expiresAt < new Date() || !s.user.active) return null;
  return s.user;
}

/** Use at the top of every page, route and server action that touches patient data. */
export async function requireUser(roles?: Role[]): Promise<User> {
  const u = await currentUser();
  if (!u) redirect("/login");
  if (roles && !roles.includes(u.role)) redirect("/?forbidden=1");
  return u;
}

export async function clientIp(): Promise<string | null> {
  const h = await headers();
  return h.get("x-forwarded-for")?.split(",")[0]?.trim() ?? h.get("x-real-ip") ?? null;
}
