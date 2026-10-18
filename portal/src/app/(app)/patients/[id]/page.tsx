import Link from "next/link";
import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { dateBr, dateTimeBr, eyeName, fmt, strategyName } from "@/lib/format";
import { presets } from "@/lib/headset";
import { ageAt, view } from "@/lib/patients";
import { trend } from "@/lib/progression";
import type { Report } from "@/lib/report-types";
import { geometry, GrayscaleMap } from "@/components/FieldMaps";
import { IntegrityBadge } from "@/components/ExamTable";
import { OrderForm } from "@/components/OrderForm";
import { TrendChart } from "@/components/TrendChart";
import { createOrderAction } from "../actions";

const sexName: Record<string, string> = { F: "feminino", M: "masculino", O: "outro" };

export default async function PatientPage({ params }: { params: Promise<{ id: string }> }) {
  const user = await requireUser();
  const { id } = await params;
  const row = await db.patient.findUnique({
    where: { id },
    include: { exams: { orderBy: { startedUtc: "asc" } }, orders: { orderBy: { createdAt: "desc" }, take: 10 } },
  });
  if (!row) notFound();
  const p = view(row);
  await audit(user.id, "patient.view", "patient", id);
  const ps = presets();
  const presetLabel = (pid: string) => ps.find((x) => x.id === pid)?.label ?? pid;

  return (
    <>
      <h1>{p.name}</h1>
      <div className="card">
        <div className="form-grid">
          <div><span className="muted">ID interno</span><br />{p.internalId}</div>
          <div><span className="muted">Código pseudônimo (headset)</span><br /><b>{p.code}</b></div>
          <div><span className="muted">Nascimento</span><br />{dateBr(p.birthDate + "T12:00:00Z")} ({fmt(ageAt(p.birthDate, new Date()), 0)} anos)</div>
          <div><span className="muted">Sexo</span><br />{p.sex ? sexName[p.sex] ?? p.sex : "—"}</div>
          <div><span className="muted">Refração</span><br />{p.refraction ?? "—"}</div>
          <div><span className="muted">Diagnóstico / observações</span><br />{p.diagnosis ?? "—"}</div>
          <div><span className="muted">Consentimento</span><br />{p.consentAt ? `registrado em ${dateTimeBr(p.consentAt)}` : <span className="alert">não registrado</span>}{p.consentNote ? ` (${p.consentNote})` : ""}</div>
          <div><span className="muted">Pesquisa</span><br />{p.researchConsent ? "autorizada (pseudonimizada)" : "não autorizada"}</div>
        </div>
        <div className="actions"><Link className="button secondary" href={`/patients/${id}/edit`}>Editar</Link></div>
      </div>

      <h2>Pedido de exame para o headset</h2>
      <div className="card">
        <OrderForm action={createOrderAction.bind(null, id)} presets={ps.map((x) => ({ id: x.id, label: x.label }))} refraction={p.refraction} />
        <p className="muted small">Copie o arquivo baixado para a pasta do app no headset com o nome <code>exam-order.json</code>. Ele leva só o código {p.code}, a idade, a correção e o exame, nunca o nome.</p>
        {row.orders.length > 0 && (
          <div className="table-wrap">
            <table>
              <thead><tr><th>Criado</th><th>Exame</th><th className="num">Idade</th><th>Situação</th><th></th></tr></thead>
              <tbody>
                {row.orders.map((o) => (
                  <tr key={o.id}>
                    <td>{dateTimeBr(o.createdAt)}</td>
                    <td>{presetLabel(o.presetId)}</td>
                    <td className="num">{fmt(o.ageYears, 1)}</td>
                    <td>{o.fulfilledAt ? `realizado (importado ${dateBr(o.fulfilledAt)})` : "aguardando"}</td>
                    <td><a href={`/patients/${id}/orders/${o.id}`} download="exam-order.json">Baixar exam-order.json</a></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {(["OD", "OS"] as const).map((eye) => {
        const exams = row.exams.filter((e) => e.eye === eye);
        if (!exams.length) return null;
        const usable = exams.filter((e) => !e.aborted && e.strategy !== "Screening");
        const md = usable.map((e) => ({ date: e.startedUtc, value: e.md }));
        const vfi = usable.map((e) => ({ date: e.startedUtc, value: e.vfi }));
        return (
          <section key={eye}>
            <h2>{eyeName(eye)}</h2>
            <div className="table-wrap">
              <table>
                <thead><tr><th>Data</th><th>Programa</th><th className="num">MD (dB)</th><th className="num">PSD (dB)</th><th className="num">VFI</th><th>GHT</th><th>Integridade</th></tr></thead>
                <tbody>
                  {exams.slice().reverse().map((e) => {
                    const r = e.report as unknown as Report;
                    return (
                      <tr key={e.id}>
                        <td><Link href={`/exams/${e.id}`}>{dateTimeBr(e.startedUtc)}</Link></td>
                        <td>{e.gridId} {strategyName[e.strategy] ?? e.strategy}{e.aborted ? <span className="alert"> (interrompido)</span> : null}{r.exam.Reliability.LowReliability ? <span className="alert"> baixa confiabilidade</span> : null}</td>
                        <td className="num">{fmt(e.md, 2)}</td>
                        <td className="num">{fmt(e.psd, 2)}</td>
                        <td className="num">{e.vfi === null ? "—" : fmt(e.vfi) + "%"}</td>
                        <td>{r.text?.ght ?? "—"}</td>
                        <td><IntegrityBadge value={e.integrity} /></td>
                      </tr>
                    );
                  })}
                </tbody>
              </table>
            </div>
            {usable.length > 0 && (
              <>
                <h2 className="small muted">Série de escala de cinza</h2>
                <div className="series card">
                  {usable.map((e) => {
                    const r = e.report as unknown as Report;
                    if (!r.analysis) return null;
                    return <Link key={e.id} href={`/exams/${e.id}`}><GrayscaleMap g={geometry(r.analysis.Points, eye)} id={`ser-${e.id}`} title={dateBr(e.startedUtc)} /></Link>;
                  })}
                </div>
                <TrendChart title="MD" unit="dB" points={md} trend={trend(md)} yMin={-10} yMax={2} />
                <TrendChart title="VFI" unit="%" points={vfi} trend={trend(vfi)} yMin={60} yMax={100} />
                <p className="muted small">Regressão linear simples dos índices (base para análise de progressão). Exames interrompidos e rastreios ficam fora. Normativo PROVISÓRIO.</p>
              </>
            )}
          </section>
        );
      })}
      {row.exams.length === 0 && <p className="muted">Nenhum exame ainda. Crie um pedido acima ou importe os arquivos do headset.</p>}
    </>
  );
}
