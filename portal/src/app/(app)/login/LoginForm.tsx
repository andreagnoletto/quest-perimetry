"use client";

import { useActionState } from "react";
import { loginAction } from "./actions";

export function LoginForm() {
  const [state, action, pending] = useActionState(loginAction, undefined);
  return (
    <form action={action} style={{ display: "grid", gap: 12 }}>
      {state?.error && <div className="notice error">{state.error}</div>}
      <label>E-mail<input name="email" type="email" autoComplete="username" required defaultValue={state?.email} key={state?.email} /></label>
      <label>Senha<input name="password" type="password" autoComplete="current-password" required /></label>
      <button type="submit" disabled={pending}>{pending ? "Entrando…" : "Entrar"}</button>
    </form>
  );
}
