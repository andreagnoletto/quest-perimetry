"use client";

import { useActionState } from "react";
import type { FormState } from "@/app/(app)/patients/actions";

export function AnonymizeForm({ action, code }: { action: (s: FormState, f: FormData) => Promise<FormState>; code: string }) {
  const [state, formAction, pending] = useActionState(action, undefined);
  return (
    <form action={formAction} className="card" style={{ maxWidth: 520 }}>
      {state?.error && <div className="notice error">{state.error}</div>}
      <div style={{ display: "grid", gap: 12 }}>
        <label>Motivo (fica no log de acesso)<input name="reason" maxLength={200} placeholder="pedido do titular em DD/MM/AAAA" /></label>
        <label>Para confirmar, digite o código do paciente ({code})<input name="confirm" autoComplete="off" required placeholder={code} /></label>
        <button type="submit" className="danger" disabled={pending}>{pending ? "Anonimizando…" : "Anonimizar definitivamente"}</button>
      </div>
    </form>
  );
}
