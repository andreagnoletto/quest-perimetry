"use server";

import { redirect } from "next/navigation";
import { login, logout, currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";

export async function loginAction(_prev: { error?: string; email?: string } | undefined, form: FormData): Promise<{ error?: string; email?: string }> {
  const email = String(form.get("email") ?? "");
  const user = await login(email, String(form.get("password") ?? ""));
  if (!user) {
    await audit(null, "login.failed", "user", undefined, email.trim().toLowerCase().slice(0, 120));
    return { error: "E-mail ou senha incorretos.", email };
  }
  await audit(user.id, "login", "user", user.id);
  redirect("/");
}

export async function logoutAction() {
  const u = await currentUser();
  await logout();
  if (u) await audit(u.id, "logout", "user", u.id);
  redirect("/login");
}
