"use client";

import { useActionState } from "react";
import type { DeviceState } from "./actions";

type Action = (s: DeviceState, f: FormData) => Promise<DeviceState>;

function Notice({ state }: { state: DeviceState }) {
  if (state?.error) return <div className="notice error">{state.error}</div>;
  if (state?.ok) return <div className="notice">{state.ok}</div>;
  return null;
}

export function DeviceForm({ action, serial, label, model, hasKey }: { action: Action; serial?: string; label?: string | null; model?: string | null; hasKey?: boolean }) {
  const [state, formAction, pending] = useActionState(action, undefined);
  return (
    <form action={formAction}>
      <Notice state={state} />
      <div className="form-grid">
        <label>Identificador (DeviceSerial do arquivo de sessão)<input name="serial" defaultValue={serial} readOnly={!!serial} required /></label>
        <label>Nome<input name="label" defaultValue={label ?? ""} placeholder="Quest 3 consultório 1" /></label>
        <label>Modelo<input name="model" defaultValue={model ?? ""} placeholder="Quest 3" /></label>
        <label>Chave de integridade (device.key){hasKey ? " — já cadastrada" : ""}<input name="hmacKey" type="password" autoComplete="off" placeholder={hasKey ? "deixe vazio para manter" : "conteúdo de device.key"} /></label>
      </div>
      <div className="actions"><button type="submit" disabled={pending}>Salvar headset</button></div>
    </form>
  );
}

export function CalibrationForm({ action }: { action: Action }) {
  const [state, formAction, pending] = useActionState(action, undefined);
  return (
    <form action={formAction}>
      <Notice state={state} />
      <div className="row">
        <label>Calibração fotométrica (LuminanceCalibration JSON)<input type="file" name="file" accept=".json,application/json" required /></label>
        <button type="submit" disabled={pending}>{pending ? "Validando…" : "Enviar e validar"}</button>
      </div>
    </form>
  );
}
