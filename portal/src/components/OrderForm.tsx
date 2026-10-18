"use client";

import { useActionState, useState } from "react";
import type { FormState } from "@/app/(app)/patients/actions";
import { fixationModes, fixationName, responseModeName, responseModes } from "@/lib/format";

export function OrderForm({ action, presets, grids, refraction, devices }: {
  action: (s: FormState, f: FormData) => Promise<FormState>;
  presets: { id: string; label: string; gridId: string; fixationMode: string }[];
  /** Grids defined in config/grids (24-2 first). */
  grids: string[];
  refraction: string | null;
  /** Paired headsets (with an active token); preselected when there is exactly one. */
  devices: { id: string; label: string }[];
}) {
  const [state, formAction, pending] = useActionState(action, undefined);
  const [presetId, setPresetId] = useState(presets[0]?.id ?? "");
  const preset = presets.find((p) => p.id === presetId) ?? presets[0];
  // Grid and fixation follow the chosen preset until the doctor changes them (Phase 5 options).
  const [gridId, setGridId] = useState(preset?.gridId ?? "24-2");
  const [fixationMode, setFixationMode] = useState(preset?.fixationMode ?? "CentralHeijlKrakau");
  const [responseMode, setResponseMode] = useState("YesNo");
  const choosePreset = (id: string) => {
    setPresetId(id);
    const p = presets.find((x) => x.id === id);
    if (p) { setGridId(p.gridId); setFixationMode(p.fixationMode); }
  };
  return (
    <form action={formAction}>
      {state?.error && <div className="notice error">{state.error}</div>}
      {state?.ok && <div className="notice">{state.ok}</div>}
      <div className="row">
        <label>Exame
          <select name="presetId" value={presetId} onChange={(e) => choosePreset(e.target.value)}>
            {presets.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
          </select>
        </label>
        <label>Grade
          <select name="gridId" value={gridId} onChange={(e) => setGridId(e.target.value)}>
            {grids.map((g) => <option key={g} value={g}>{g}</option>)}
          </select>
        </label>
        <label>Monitor de fixação
          <select name="fixationMode" value={fixationMode} onChange={(e) => setFixationMode(e.target.value)}>
            {fixationModes.map((m) => <option key={m} value={m}>{fixationName[m]}</option>)}
          </select>
        </label>
        <label>Resposta
          <select name="responseMode" value={responseMode} onChange={(e) => setResponseMode(e.target.value)}>
            {responseModes.map((m) => <option key={m} value={m}>{responseModeName[m]}</option>)}
          </select>
        </label>
        <label className="check"><input type="checkbox" name="fovealThreshold" /> Limiar foveal</label>
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
      {(fixationMode === "CentralEyeTracking" || fixationMode === "Oculokinetic" || responseMode === "ForcedChoice") && (
        <p className="muted small">
          {fixationMode === "CentralEyeTracking" ? "Eye-tracking exige um headset com rastreamento ocular (Quest Pro); sem ele o headset usa o monitor do preset. " : ""}
          {fixationMode === "Oculokinetic" ? "Fixação oculocinética (modo B): o alvo salta antes de cada estímulo; a grade é calculada em relação ao alvo. " : ""}
          {responseMode === "ForcedChoice" ? "Escolha forçada é experimental: dois intervalos por apresentação, sem falsos positivos nem Heijl-Krakau; exame cerca de 3× mais longo." : ""}
        </p>
      )}
    </form>
  );
}
