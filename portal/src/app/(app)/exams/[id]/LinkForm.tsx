"use client";

import { useActionState } from "react";
import type { LinkState } from "../actions";

export function LinkForm({ action, code }: { action: (s: LinkState, f: FormData) => Promise<LinkState>; code: string | null }) {
  const [state, formAction, pending] = useActionState(action, undefined);
  return (
    <form action={formAction} className="row">
      <label>Código do paciente<input name="code" defaultValue={code ?? ""} placeholder="P-XXXXXX" maxLength={12} /></label>
      <button type="submit" className="secondary" disabled={pending}>{code ? "Alterar vínculo" : "Vincular"}</button>
      {state?.error && <span className="alert">{state.error}</span>}
      {state?.ok && <span className="ok">{state.ok}</span>}
    </form>
  );
}
