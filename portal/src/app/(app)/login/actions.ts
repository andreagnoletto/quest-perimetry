"use server";

import { redirect } from "next/navigation";
import { changePassword, clientIp, currentUser, login, logout, requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { hit, LOGIN_IP_LIMIT, minutesLeft } from "@/lib/rate-limit";

export async function loginAction(_prev: { error?: string; email?: string } | undefined, form: FormData): Promise<{ error?: string; email?: string }> {
  const email = String(form.get("email") ?? "");
  const ip = (await clientIp()) ?? "local";
  const rate = hit("login:" + ip, LOGIN_IP_LIMIT.limit, LOGIN_IP_LIMIT.windowMs);
  if (!rate.allowed) {
    await audit(null, "login.ratelimited", "user", undefined, `ip ${ip.slice(0, 60)}`);
    return { error: `Muitas tentativas deste endereço. Tente novamente em ${minutesLeft(rate.retryAfterMs)} min.`, email };
  }
  const r = await login(email, String(form.get("password") ?? ""));
  if ("error" in r) {
    const who = email.trim().toLowerCase().slice(0, 120);
    if (r.error === "locked") {
      await audit(null, "login.locked", "user", undefined, who);
      return { error: `Conta bloqueada temporariamente por excesso de tentativas. Tente novamente em ${minutesLeft(r.until.getTime() - Date.now())} min.`, email };
    }
    await audit(null, "login.failed", "user", undefined, who);
    return { error: "E-mail ou senha incorretos.", email };
  }
  await audit(r.user.id, "login", "user", r.user.id);
  redirect("/");
}

export async function logoutAction() {
  const u = await currentUser();
  await logout();
  if (u) await audit(u.id, "logout", "user", u.id);
  redirect("/login");
}

export type PasswordState = { error?: string; ok?: string } | undefined;

export async function changePasswordAction(_: PasswordState, form: FormData): Promise<PasswordState> {
  const user = await requireUser();
  const next = String(form.get("next") ?? "");
  if (next !== String(form.get("confirm") ?? "")) return { error: "A confirmação não confere com a nova senha." };
  const problem = await changePassword(user, String(form.get("current") ?? ""), next);
  if (problem) return { error: problem };
  await audit(user.id, "user.password", "user", user.id, "other sessions ended");
  return { ok: "Senha alterada. As outras sessões desta conta foram encerradas." };
}
