import { requireUser } from "@/lib/auth";
import { db } from "@/lib/db";
import { dateBr, dateTimeBr, fmt } from "@/lib/format";
import type { CalibrationCheck } from "@/lib/perimetry";
import { createPairingAction, revokeTokenAction, saveDeviceAction, uploadCalibrationAction } from "./actions";
import { CalibrationForm, DeviceForm, PairingForm } from "./Forms";

export default async function Devices() {
  const user = await requireUser();
  const canEdit = user.role !== "operator";
  const devices = await db.device.findMany({ orderBy: { createdAt: "asc" }, include: { calibrations: { orderBy: { uploadedAt: "desc" }, take: 5 }, tokens: { orderBy: { createdAt: "desc" }, take: 5 } } });
  const now = new Date();
  return (
    <>
      <h1>Headsets e calibração</h1>
      <p className="muted">Um headset aparece aqui ao parear pela rede, na primeira importação de um arquivo de sessão, ou cadastrado à mão. Com a chave <code>device.key</code> cadastrada, o portal confere a assinatura de cada exame. Sem calibração fotométrica válida, os laudos saem como <b>NÃO CALIBRADO</b>.</p>
      {devices.map((d) => {
        const active = d.calibrations.find((c) => c.active);
        const expired = active?.validUntil ? active.validUntil < now : false;
        return (
          <section key={d.id} className="card">
            <h2 style={{ marginTop: 0 }}>{d.label ?? d.model ?? "Headset"} <span className="muted small">{d.serial}</span></h2>
            <p>Integridade: {d.hmacKeyEnc ? <span className="ok">chave cadastrada</span> : <span className="alert">sem chave (exames ficam “não verificados”)</span>}</p>
            <p>Sincronização: {d.tokens.some((t) => !t.revokedAt)
              ? <><span className="ok">pareado</span>{d.tokens.filter((t) => !t.revokedAt).map((t) => <span key={t.id}> · token de {dateTimeBr(t.createdAt)}{t.lastUsedAt ? `, usado ${dateTimeBr(t.lastUsedAt)}` : ", nunca usado"}{t.software ? ` (${t.software})` : ""}{canEdit && <form action={revokeTokenAction.bind(null, t.id)} style={{ display: "inline" }}> <button className="link" type="submit">revogar</button></form>}</span>)}</>
              : <span className="muted">não pareado (só importação manual)</span>}</p>
            <p>Calibração: {active
              ? <>{expired ? <span className="alert">VENCIDA</span> : <span className="ok">ativa</span>} · medida {active.measuredAt ? dateBr(active.measuredAt) : "—"} · válida até {active.validUntil ? dateBr(active.validUntil) : "—"} · {active.photometer ?? "fotômetro não informado"} · <a href={`/devices/calibrations/${active.id}`} download="calibration.json">baixar calibration.json</a></>
              : <span className="alert">nenhuma (NÃO CALIBRADO)</span>}</p>
            {d.calibrations.length > 0 && (
              <div className="table-wrap">
                <table>
                  <thead><tr><th>Enviada</th><th>Medida</th><th>Validade</th><th>Faixa medida (cd/m²)</th><th></th></tr></thead>
                  <tbody>
                    {d.calibrations.map((c) => {
                      const s = c.summary as unknown as CalibrationCheck;
                      return (
                        <tr key={c.id}>
                          <td>{dateTimeBr(c.uploadedAt)}</td>
                          <td>{c.measuredAt ? dateBr(c.measuredAt) : "—"}</td>
                          <td>{c.validUntil ? dateBr(c.validUntil) : "—"}</td>
                          <td>{Object.entries(s.eyes ?? {}).map(([eye, v]) => `${eye} ${fmt(v.minCdm2, 2)}–${fmt(v.maxCdm2, 1)} (${v.samples})`).join(" · ")}</td>
                          <td>{c.active ? "ativa" : "substituída"}</td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
            )}
            {canEdit && (
              <>
                <h2 className="small">Enviar nova calibração</h2>
                <CalibrationForm action={uploadCalibrationAction.bind(null, d.id)} />
                <h2 className="small">Dados do headset</h2>
                <DeviceForm action={saveDeviceAction} serial={d.serial} label={d.label} model={d.model} hasKey={!!d.hmacKeyEnc} />
              </>
            )}
          </section>
        );
      })}
      {canEdit && (
        <section className="card">
          <h2 style={{ marginTop: 0 }}>Parear headset pela rede</h2>
          <PairingForm action={createPairingAction} />
        </section>
      )}
      {canEdit && (
        <section className="card">
          <h2 style={{ marginTop: 0 }}>Cadastrar headset</h2>
          <DeviceForm action={saveDeviceAction} />
        </section>
      )}
    </>
  );
}
