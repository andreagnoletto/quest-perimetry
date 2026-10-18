import type { Report } from "@/lib/report-types";
import { dateBr, dateTimeBr, duration, eyeName, fmt, pct, pLabel, strategyName } from "@/lib/format";
import { fixationLine, fixationTargetLine, fovealLine, gazeBar } from "@/lib/report-format";
import { geometry, GrayscaleMap, NumericMap, ProbabilityLegend, ProbabilityMap, ScreeningMap } from "./FieldMaps";
import { GazeTraceBar } from "./GazeTraceBar";

// HFA "Single Field Analysis" layout (SPEC §7), one A4 page per eye. Every number comes from the /core
// report; this component only lays it out. Thresholds are shown in HFA-equivalent dB, the scale of the
// normative and grayscale; values below the device floor print as "<NN".

export interface ReportPatient { name: string; internalId: string; birthDate: string; code: string }

export interface ReportDevice { model: string | null; serial: string; label: string | null }

/** Footer text for the headset that recorded the exam: "aparelho Meta Quest 3 (S/N …)". */
export function deviceLine(d: ReportDevice | null | undefined): string {
  if (!d) return "";
  const name = d.model ?? d.label ?? "modelo não informado";
  return ` · aparelho ${name} (S/N ${d.serial})`;
}

export function SingleFieldReport({ report, patient, device, integrity, footerNote }: {
  report: Report;
  patient?: ReportPatient | null;
  device?: ReportDevice | null;
  integrity?: "verified" | "unverified" | "failed";
  footerNote?: string;
}) {
  const e = report.exam;
  const a = report.analysis;
  const r = e.Reliability;
  const s = e.Settings;
  const floor = Math.round(e.MinMeasurableHfaDb);
  const catchText = (c: { Trials: number; Errors: number }) => (c.Trials === 0 ? "—" : `${c.Errors}/${c.Trials}`);
  const red = (bad: boolean) => (bad ? "alert" : undefined);
  const started = new Date(e.StartedUtc);
  const program = `Central ${s.GridId} — ${strategyName[s.Strategy] ?? s.Strategy}`;
  const foveal = fovealLine(report, floor);
  const gaze = gazeBar(report);
  const id = e.ExamId.replace(/[^A-Za-z0-9-]/g, "");

  return (
    <article className="sfa" data-exam={e.ExamId}>
      <header className="sfa-head">
        <div>
          <h1>Análise de campo único</h1>
          <div className="program">{program}</div>
        </div>
        <div className="eye">{eyeName(s.Eye)}</div>
      </header>

      <section className="sfa-patient">
        <div><b>Paciente:</b> {patient?.name ?? "não vinculado"}</div>
        <div><b>ID:</b> {patient?.internalId ?? "—"} <span className="muted">({patient?.code ?? s.PatientRef ?? "sem código"})</span></div>
        <div><b>Nascimento:</b> {patient ? dateBr(patient.birthDate + "T12:00:00Z") : "—"}</div>
        <div><b>Idade:</b> {fmt(s.AgeYears, 0)}</div>
        <div><b>Data:</b> {dateTimeBr(started)}</div>
      </section>

      <section className="sfa-params">
        <dl>
          <dt>Monitor de fixação</dt><dd>{fixationLine(report)}</dd>
          <dt>Alvo de fixação</dt><dd>{fixationTargetLine(report)}</dd>
          <dt>Perdas de fixação</dt><dd className={red(r.FixationLosses.Alert ?? false)}>{catchText(r.FixationLosses)}{r.FixationLosses.Alert ? " xx" : ""}</dd>
          {gaze && <><dt>Olhar descartado</dt><dd>{gaze.rejected}{gaze.samples ? ` de ${gaze.samples}` : ""}</dd></>}
          <dt>Falsos positivos</dt><dd className={red(r.FalsePositives.Alert ?? false)}>{r.FalsePositives.Trials ? pct(r.FalsePositiveRate) : "—"}{r.FalsePositives.Alert ? " xx" : ""}</dd>
          <dt>Falsos negativos</dt><dd className={red(r.FalseNegatives.Alert ?? false)}>{r.FalseNegatives.Trials ? pct(r.FalseNegativeRate) : "—"}{r.FalseNegatives.Alert ? " xx" : ""}</dd>
          <dt>Duração</dt><dd>{duration(r.EffectiveDurationMs)}{r.PausedMs > 0 ? ` (+${duration(r.PausedMs)} em pausa)` : ""}</dd>
          <dt>Mancha cega</dt><dd>{r.BlindSpotFound ? `(${fmt(r.BlindSpotX, 1)}°, ${fmt(r.BlindSpotY, 1)}°)` : "não localizada"}{r.BlindSpotRemaps ? ` · relocalizada ${r.BlindSpotRemaps}×` : ""}</dd>
        </dl>
        <dl>
          <dt>Estímulo</dt><dd>{s.StimulusSize}, branco ({fmt(e.StimulusDiameterDeg, 2)}°, {e.StimulusDurationMs} ms)</dd>
          <dt>Fundo</dt><dd>{fmt(e.BackgroundAsb, 1)} asb ({fmt(e.BackgroundCdm2, 0)} cd/m²)</dd>
          <dt>Estratégia</dt><dd>{strategyName[s.Strategy] ?? s.Strategy}</dd>
          <dt>Limiar foveal</dt><dd className="foveal" data-tested={foveal.tested}>{foveal.value}{foveal.p ? <> <span className="p">{foveal.p}</span></> : null}</dd>
          <dt>Correção</dt><dd>{s.RefractiveCorrection || "não informada"}</dd>
          <dt>Calibração</dt><dd className={red(!e.Calibrated)}>{e.Calibrated ? e.CalibrationRef : "NÃO CALIBRADO"}</dd>
          <dt>Escala</dt><dd>dB equiv. HFA = dB do aparelho + {fmt(e.HfaOffsetDb, 1)}; mínimo mensurável {floor} dB</dd>
          <dt>Apresentações</dt><dd>{r.TotalPresentations}{r.AbortedPresentations ? ` (${r.AbortedPresentations} interrompidas)` : ""}</dd>
        </dl>
      </section>

      <section className="sfa-warnings" aria-label="Avisos">
        {report.warnings.map((w) => <p key={w}>{w}</p>)}
        {integrity === "failed" && <p>Assinatura do arquivo NÃO confere — o arquivo pode ter sido alterado</p>}
      </section>

      {!a ? (
        <p className="sfa-empty">Exame interrompido: sem análise.</p>
      ) : s.Strategy === "Screening" ? (
        <section className="sfa-grid one">
          <ScreeningMap g={geometry(a.Points, s.Eye)} title="Rastreio (○ visto · r visto no reteste · ■ não visto)" id={`${e.ExamId}-scr`} />
        </section>
      ) : (
        <Maps report={report} floor={floor} />
      )}

      {gaze && <GazeTraceBar g={gaze} id={`${id}-gaze`} />}

      <footer className="sfa-foot">
        <span>{e.Disclaimer}</span>
        <span>
          Exame {e.ExamId} · headset {e.SoftwareVersion}{deviceLine(device)} · análise {report.analyzedWith.software} · config {report.analyzedWith.configHash.slice(0, 12)}
          {integrity ? ` · integridade: ${integrity === "verified" ? "verificada" : integrity === "failed" ? "FALHOU" : "não verificada"}` : ""}
          {footerNote ? ` · ${footerNote}` : ""}
        </span>
      </footer>
    </article>
  );
}

function Maps({ report, floor }: { report: Report; floor: number }) {
  const a = report.analysis!;
  const eye = report.exam.Settings.Eye;
  const g = geometry(a.Points, eye);
  const id = report.exam.ExamId.replace(/[^A-Za-z0-9-]/g, "");
  return (
    <>
      <section className="sfa-grid two">
        <NumericMap g={g} id={`${id}-thr`} title="Limiares (dB equiv. HFA)"
          value={(p) => (p.CensoredLow ? `<${floor}` : p.HfaEqDb === null ? "" : fmt(p.HfaEqDb))} />
        <GrayscaleMap g={g} id={`${id}-gray`} title="Escala de cinza" />
      </section>
      <section className="sfa-grid three">
        <NumericMap g={g} id={`${id}-td`} title="Desvio total" value={(p) => (p.IsBlindSpot ? "" : fmt(p.Td))} />
        <NumericMap g={g} id={`${id}-pd`} title="Desvio padrão (pattern)" value={(p) => (p.IsBlindSpot ? "" : fmt(p.Pd))} />
        <div className="sfa-indices">
          <p className="ght" data-ght={a.Ght}><b>GHT</b><br />{a.GhtApplicable === false ? "Não se aplica" : report.text?.ght}</p>
          <p><b>VFI</b> {fmt(a.Vfi, 1)}%</p>
          <p><b>MD</b> {fmt(a.Md, 2)} dB <span className="p">{pLabel(a.MdP)}</span></p>
          <p><b>PSD</b> {fmt(a.Psd, 2)} dB <span className="p">{pLabel(a.PsdP)}</span></p>
          {a.NormativeProvisional && <p className="alert small">Índices e probabilidades com normativo PROVISÓRIO</p>}
        </div>
        <ProbabilityMap g={g} id={`${id}-tdp`} title="" value={(p) => p.TdP} />
        <ProbabilityMap g={g} id={`${id}-pdp`} title="" value={(p) => p.PdP} />
        <ProbabilityLegend id={`${id}-leg`} />
      </section>
    </>
  );
}
