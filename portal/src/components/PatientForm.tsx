"use client";

import { useActionState } from "react";
import type { FormState } from "@/app/(app)/patients/actions";
import { CONSENT_TEXT, CONSENT_VERSION, RESEARCH_CONSENT_TEXT } from "@/lib/consent-text";

export interface PatientDefaults { internalId?: string; name?: string; birthDate?: string; sex?: string | null; refraction?: string | null; diagnosis?: string | null; researchConsent?: boolean }

export function PatientForm({ action, defaults, isNew }: {
  action: (s: FormState, f: FormData) => Promise<FormState>;
  defaults?: PatientDefaults;
  isNew: boolean;
}) {
  const [state, formAction, pending] = useActionState(action, undefined);
  const d = defaults ?? {};
  return (
    <form action={formAction} className="card">
      {state?.error && <div className="notice error">{state.error}</div>}
      <div className="form-grid">
        <label>Nome completo<input name="name" defaultValue={d.name} required maxLength={200} /></label>
        <label>ID interno (prontuário)<input name="internalId" defaultValue={d.internalId} required maxLength={64} /></label>
        <label>Data de nascimento<input name="birthDate" type="date" defaultValue={d.birthDate} required /></label>
        <label>Sexo
          <select name="sex" defaultValue={d.sex ?? ""}>
            <option value="">não informado</option><option value="F">Feminino</option><option value="M">Masculino</option><option value="O">Outro</option>
          </select>
        </label>
        <label>Refração / correção usada no exame<input name="refraction" defaultValue={d.refraction ?? ""} placeholder="+2,00 esf" maxLength={100} /></label>
        <label>Diagnóstico / observações<input name="diagnosis" defaultValue={d.diagnosis ?? ""} maxLength={500} /></label>
      </div>
      <div style={{ display: "grid", gap: 8, marginTop: 14 }}>
        {isNew && (
          <>
            <p className="muted small" style={{ margin: 0 }}>{CONSENT_TEXT} <span className="muted">(texto v{CONSENT_VERSION}, gravado no registro)</span></p>
            <label className="check"><input type="checkbox" name="consent" required /> O paciente consentiu com o exame e com o armazenamento dos dados (LGPD)</label>
            <label>Observação do consentimento (opcional)<input name="consentNote" maxLength={300} placeholder="termo assinado em papel, nº…" /></label>
          </>
        )}
        <p className="muted small" style={{ margin: 0 }}>{RESEARCH_CONSENT_TEXT}</p>
        <label className="check"><input type="checkbox" name="researchConsent" defaultChecked={d.researchConsent} /> Autoriza uso pseudonimizado dos exames em pesquisa</label>
      </div>
      <div className="actions"><button type="submit" disabled={pending}>{pending ? "Salvando…" : "Salvar"}</button></div>
      <p className="muted small">Nome, ID interno, nascimento, sexo, refração e diagnóstico são gravados criptografados. O headset recebe só o código pseudônimo.</p>
    </form>
  );
}
