"use client";

import { useActionState } from "react";
import { changePasswordAction } from "../login/actions";

export function PasswordForm({ minLength }: { minLength: number }) {
  const [state, action, pending] = useActionState(changePasswordAction, undefined);
  return (
    <form action={action} className="card" style={{ maxWidth: 420 }}>
      {state?.error && <div className="notice error">{state.error}</div>}
      {state?.ok && <div className="notice">{state.ok}</div>}
      <div style={{ display: "grid", gap: 12 }}>
        <label>Senha atual<input name="current" type="password" autoComplete="current-password" required /></label>
        <label>Nova senha (mínimo {minLength} caracteres)<input name="next" type="password" autoComplete="new-password" minLength={minLength} required /></label>
        <label>Confirmar nova senha<input name="confirm" type="password" autoComplete="new-password" minLength={minLength} required /></label>
        <button type="submit" disabled={pending}>{pending ? "Salvando…" : "Trocar senha"}</button>
      </div>
    </form>
  );
}
