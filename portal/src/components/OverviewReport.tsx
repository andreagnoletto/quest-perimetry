import type { Overview, OverviewGrid } from "@/lib/overview";
import { dateBr, dateTimeBr, eyeName, fmt, pct, pLabel, strategyName } from "@/lib/format";
import { geometry, GrayscaleMap, ProbabilityLegend, ProbabilityMap } from "./FieldMaps";
import { TrendChart } from "./TrendChart";

// HFA-style "Overview" of one eye: the exams in chronological order, each as a small grayscale map and the total
// deviation probability map with its indices and reliability, then the MD and VFI trend. Every number comes from
// the /core report of each exam; only the ordering and the OLS line (lib/progression) happen in the portal.

const DISCLAIMER = "Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA";

export function OverviewReport({ o, footerNote }: { o: Overview; footerNote?: string }) {
  const total = o.grids.reduce((n, g) => n + g.exams.length, 0);
  return (
    <article className="sfa ovw" data-eye={o.eye}>
      <header className="sfa-head">
        <div>
          <h1>Visão geral — progressão</h1>
          <div className="program">{total} exames de limiar em ordem cronológica · escala de cinza e probabilidade do desvio total</div>
        </div>
        <div className="eye">{eyeName(o.eye)}</div>
      </header>

      <section className="sfa-patient">
        <div><b>Paciente:</b> {o.patient.name}</div>
        <div><b>ID:</b> {o.patient.internalId} <span className="muted">({o.patient.code})</span></div>
        <div><b>Nascimento:</b> {dateBr(o.patient.birthDate + "T12:00:00Z")}</div>
        <div><b>Emitido:</b> {dateTimeBr(new Date())}</div>
      </section>

      {total === 0 && <p className="sfa-empty">Nenhum exame de limiar concluído para este olho.</p>}
      {o.grids.map((g) => <GridSeries key={g.gridId} g={g} eye={o.eye} />)}

      <footer className="sfa-foot">
        <span className="sfa-disclaimer">{DISCLAIMER}</span>
        <span className="sfa-note">Tendência: regressão linear simples de MD e VFI contra o tempo (inclinação/ano ± EP, p bicaudal; p a partir de 3 exames). Descritiva: não é análise de eventos (GPA).</span>
        {o.omitted.length > 0 && <span className="sfa-note">Fora da série: {o.omitted.map((x) => `${dateBr(x.exam.startedUtc)} (${x.reason})`).join(", ")}.</span>}
        <span>
          {o.normatives.length ? o.normatives.join(" · ") : "sem análise"} · config {o.configs.join(", ") || "—"}
          {footerNote ? ` · ${footerNote}` : ""}
        </span>
      </footer>
    </article>
  );
}

function GridSeries({ g, eye }: { g: OverviewGrid; eye: Overview["eye"] }) {
  return (
    <section className="ovw-grid" data-grid={g.gridId}>
      <h2>Central {g.gridId}</h2>
      <div className="ovw-series">
        {g.exams.map(({ exam, report }) => {
          const a = report.analysis!;
          const r = report.exam.Reliability;
          const geo = geometry(a.Points, eye);
          const id = `ov-${exam.id}`;
          return (
            <div key={exam.id} className="ovw-exam" data-exam={report.exam.ExamId}>
              <div className="ovw-date"><b>{dateBr(exam.startedUtc)}</b> <span className="muted">{strategyName[exam.strategy] ?? exam.strategy}</span></div>
              <GrayscaleMap g={geo} id={`${id}-gray`} title="" />
              <ProbabilityMap g={geo} id={`${id}-tdp`} title="" value={(p) => p.TdP} />
              <dl className="ovw-idx">
                <dt>MD</dt><dd>{fmt(a.Md, 2)} dB {pLabel(a.MdP) && <span className="p">{pLabel(a.MdP)}</span>}</dd>
                <dt>PSD</dt><dd>{fmt(a.Psd, 2)} dB {pLabel(a.PsdP) && <span className="p">{pLabel(a.PsdP)}</span>}</dd>
                <dt>VFI</dt><dd>{fmt(a.Vfi, 1)}%</dd>
                <dt>GHT</dt><dd>{a.GhtApplicable === false ? "n/a" : report.text?.ght ?? "—"}</dd>
                <dt>PF</dt><dd className={r.FixationLosses.Alert ? "alert" : undefined}>{r.FixationLosses.Trials ? `${r.FixationLosses.Errors}/${r.FixationLosses.Trials}` : "—"}{r.FixationLosses.Alert ? " xx" : ""}</dd>
                <dt>FP</dt><dd className={r.FalsePositives.Alert ? "alert" : undefined}>{r.FalsePositives.Trials ? pct(r.FalsePositiveRate) : "—"}{r.FalsePositives.Alert ? " xx" : ""}</dd>
                <dt>FN</dt><dd className={r.FalseNegatives.Alert ? "alert" : undefined}>{r.FalseNegatives.Trials ? pct(r.FalseNegativeRate) : "—"}{r.FalseNegatives.Alert ? " xx" : ""}</dd>
              </dl>
              {r.LowReliability && <div className="alert small">baixa confiabilidade</div>}
            </div>
          );
        })}
      </div>
      <ProbabilityLegend id={`ov-${g.gridId}-leg`} />
      <div className="ovw-trends">
        <TrendChart title={`MD (${g.gridId})`} unit="dB" points={g.mdPoints} trend={g.md} yMin={-10} yMax={2} />
        <TrendChart title={`VFI (${g.gridId})`} unit="%" points={g.vfiPoints} trend={g.vfi} yMin={60} yMax={100} />
      </div>
    </section>
  );
}
