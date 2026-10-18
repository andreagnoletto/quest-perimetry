"use client";

import { useActionState } from "react";
import type { FormState } from "@/app/(app)/patients/actions";

export function OrderForm({ action, presets, refraction, devices }: {
  action: (s: FormState, f: FormData) => Promise<FormState>;
  presets: { id: string; label: string }[];
  refraction: string | null;
  /** Paired headsets (with an active token); preselected when there is exactly one. */
  devices: { id: string; label: string }[];
}) {
  const [state, formAction, pending] = useActionState(action, undefined);
  return (
    <form action={formAction}>
      {state?.error && <div className="notice error">{state.error}</div>}
      {state?.ok && <div className="notice">{state.ok}</div>}
      <div className="row">
        <label>Exame
          <select name="presetId" defaultValue={presets[0]?.id}>
            {presets.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
          </select>
        </label>
        <label>Correção no exame<input name="refraction" defaultValue={refraction ?? ""} placeholder="+2,00 esf" maxLength={100} /></label>
        <label>Semente (opcional)<input name="seed" inputMode="numeric" pattern="\d*" maxLength={9} style={{ width: 110 }} /></label>
        <label>Enviar para o headset
          <select name="deviceId" defaultValue={devices.length === 1 ? devices[0].id : ""}>
            <option value="">só baixar o arquivo</option>
            {devices.map((d) => <option key={d.id} value={d.id}>{d.label}</option>)}
          </select>
        </label>
        <button type="submit" disabled={pending}>Criar pedido de exame</button>
      </div>
    </form>
  );
}
