"use client";

import Link from "next/link";
import { useActionState } from "react";
import { importAction } from "../actions";

const statusName = { imported: "importado", duplicate: "já existia", error: "erro", skipped: "—" } as const;
const integrityName = { verified: "verificada", unverified: "não verificada", failed: "FALHOU" } as const;

export function ImportForm({ devices, patients }: { devices: { id: string; label: string }[]; patients: { id: string; label: string }[] }) {
  const [state, action, pending] = useActionState(importAction, undefined);
  return (
    <>
      <form action={action} className="card">
        {state?.error && <div className="notice error">{state.error}</div>}
        <div className="form-grid">
          <label>Arquivos do headset (pasta exams/)
            <input type="file" name="files" multiple accept=".json,.hmac,application/json" required />
          </label>
          <label>Headset
            <select name="deviceId" defaultValue="">
              <option value="">identificar pelo arquivo de sessão</option>
              {devices.map((d) => <option key={d.id} value={d.id}>{d.label}</option>)}
            </select>
          </label>
          <label>Paciente
            <select name="patientId" defaultValue="">
              <option value="">pelo código gravado no exame</option>
              {patients.map((p) => <option key={p.id} value={p.id}>{p.label}</option>)}
            </select>
          </label>
        </div>
        <div className="actions"><button type="submit" disabled={pending}>{pending ? "Importando e analisando…" : "Importar"}</button></div>
        <p className="muted small">Envie juntos o exame, o arquivo de sessão e as assinaturas <code>.hmac</code>. Com a chave do headset cadastrada, a integridade de cada arquivo é verificada. A análise é feita pelo mesmo código do headset (/core).</p>
      </form>
      {state?.outcomes && (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Arquivo</th><th>Tipo</th><th>Resultado</th><th>Integridade</th><th>Detalhe</th></tr></thead>
            <tbody>
              {state.outcomes.filter((o) => o.kind !== "hmac").map((o) => (
                <tr key={o.file}>
                  <td>{o.examDbId ? <Link href={`/exams/${o.examDbId}`}>{o.file}</Link> : o.file}</td>
                  <td>{o.kind === "exam" ? "exame" : o.kind === "session" ? "sessão" : "?"}</td>
                  <td className={o.status === "error" ? "alert" : undefined}>{statusName[o.status]}</td>
                  <td>{o.integrity ? <span className={`badge ${o.integrity}`}>{integrityName[o.integrity]}</span> : "—"}</td>
                  <td>{o.message}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
