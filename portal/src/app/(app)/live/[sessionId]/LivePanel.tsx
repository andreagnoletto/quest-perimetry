"use client";

import Link from "next/link";
import { useEffect, useMemo, useState, useTransition } from "react";
import { Frame, geometry } from "@/components/FieldMaps";
import { dateTimeBr, duration, eyeName, fixationName, fmt, responseModeName, strategyName } from "@/lib/format";
import { LIVE_ACTIVE_MS, liveStateName, type LiveCatch, type LiveCommandType, type LiveView } from "@/lib/live-types";
import { liveCommandAction, type CommandState } from "../actions";

/**
 * Operator screen of a running headset session (the HFA operator display): state, map filling in with the
 * thresholds finished so far (HFA-equivalent dB, "<NN" below the device floor), the last stimulus, reliability
 * counters, and pause / resume / abort. Updates arrive over Server-Sent Events.
 */
export function LivePanel({ initial, patientName, patientId, presetLabel }: { initial: LiveView; patientName: string | null; patientId: string | null; presetLabel: string }) {
  const [view, setView] = useState(initial);
  const [connected, setConnected] = useState(true);
  const [now, setNow] = useState(() => Date.now());
  const [msg, setMsg] = useState<CommandState>(undefined);
  const [pending, start] = useTransition();

  useEffect(() => {
    const es = new EventSource(`/api/live/${initial.sessionId}/stream`);
    es.onmessage = (e) => { setView(JSON.parse(e.data) as LiveView); setConnected(true); };
    es.onerror = () => setConnected(false);
    const t = setInterval(() => setNow(Date.now()), 1000);
    return () => { es.close(); clearInterval(t); };
  }, [initial.sessionId]);

  const s = view.snapshot;
  const stale = now - new Date(view.receivedUtc).getTime() > LIVE_ACTIVE_MS;
  const ex = s.exam;
  const floor = ex ? Math.round(ex.minMeasurableHfaDb) : 0;
  const g = useMemo(() => (ex ? geometry(ex.locations.map((l) => ({ Index: l.i, X: l.x, Y: l.y, l })), ex.eye) : null), [ex]);
  const finished = ex ? ex.locations.filter((l) => l.state === "Finished" && !l.blindSpot).length : 0;
  const total = ex ? ex.locations.filter((l) => !l.blindSpot).length : 0;

  const send = (type: LiveCommandType) => {
    if (type === "abort" && !window.confirm("Encerrar a sessão no headset? O olho em andamento será salvo como interrompido.")) return;
    start(async () => setMsg(await liveCommandAction(s.sessionId, type)));
  };

  return (
    <div className="live" data-state={s.state} data-seq={s.seq}>
      <div className="card live-head">
        <div>
          <div className="live-state">{s.finished ? (s.outcome === "aborted" ? "Interrompido" : "Concluído") : liveStateName[s.state] ?? s.state}{s.paused && !s.finished ? " (pausado)" : ""}</div>
          <div className="muted small">
            {connected && !stale ? <><span className="live-dot" />transmitindo · último envio {dateTimeBr(view.receivedUtc)}</> : <span className="alert">sem dados do headset há {duration(now - new Date(view.receivedUtc).getTime())}</span>}
            {view.deviceLabel ? ` · ${view.deviceLabel}` : ""}
          </div>
        </div>
        <dl>
          <dt>Paciente</dt><dd>{patientId ? <Link href={`/patients/${patientId}`}>{patientName}</Link> : patientName ?? "—"} <span className="muted">{s.patientRef ?? "sem pedido"}</span></dd>
          <dt>Exame</dt><dd>{presetLabel}{ex ? ` · ${ex.gridId} ${strategyName[ex.strategy] ?? ex.strategy}` : ""}{ex?.fixationMode ? ` · ${fixationName[ex.fixationMode] ?? ex.fixationMode}` : ""}{ex?.responseMode && ex.responseMode !== "YesNo" ? ` · ${responseModeName[ex.responseMode] ?? ex.responseMode}` : ""}</dd>
          {ex?.fovealThreshold && <><dt>Limiar foveal</dt><dd>{!ex.foveal ? "em teste" : `${ex.foveal.censoredLow ? "<" : ""}${fmt(ex.foveal.hfaEqDb)} dB`}</dd></>}
          <dt>Olho</dt><dd>{s.eye ? eyeName(s.eye) : "—"}</dd>
          <dt>Idade</dt><dd>{s.ageYears === null ? "—" : fmt(s.ageYears, 0)}</dd>
          <dt>Tempo</dt><dd>{duration(s.elapsedMs)}{ex ? ` · ${ex.reliability.totalPresentations} apresentações · ${finished}/${total} pontos` : ""}</dd>
          <dt>Início</dt><dd>{dateTimeBr(s.startedUtc)}</dd>
        </dl>
        <div className="live-buttons">
          <button type="button" disabled={pending || s.finished || s.state !== "Exam"} onClick={() => send("pause")}>Pausar</button>
          <button type="button" className="secondary" disabled={pending || s.finished || s.state !== "Paused"} onClick={() => send("resume")}>Retomar</button>
          <button type="button" className="danger" disabled={pending || s.finished} onClick={() => send("abort")}>Encerrar</button>
          {view.pendingCommands.length > 0 && <span className="muted small">aguardando o headset: {view.pendingCommands.map((c) => c.type).join(", ")}</span>}
          {msg?.error && <span className="alert small">{msg.error}</span>}
          {msg?.ok && <span className="ok small">{msg.ok}</span>}
        </div>
      </div>

      <div className="live-grid">
        <div className="card">
          {ex && g ? (
            <Frame g={g} id="live-map" title={`Limiares (dB equiv. HFA) · ${eyeName(ex.eye)}`}>
              {g.points.map((p) => {
                const l = p.l;
                const fs = (p.cell === g.spacing ? g.spacing : Math.max(g.fine * 0.75, g.spacing * 0.25)) * 0.42;
                if (l.blindSpot) return <circle key={p.Index} cx={p.dx} cy={-p.Y} r={fs * 0.35} fill="none" stroke="#999" strokeWidth={0.2} />;
                if (l.state === "Finished") {
                  const v = l.censoredLow ? `<${floor}` : l.hfaEqDb === null ? "" : fmt(l.hfaEqDb);
                  return <text key={p.Index} x={p.dx} y={-p.Y} fontSize={fs} textAnchor="middle" dominantBaseline="central" className="num">{v}</text>;
                }
                return <circle key={p.Index} cx={p.dx} cy={-p.Y} r={l.state === "Active" ? fs * 0.3 : fs * 0.12} fill={l.state === "Active" ? "#1f5fa8" : "#bbb"} />;
              })}
              {ex.last && (
                <circle cx={ex.last.x} cy={-ex.last.y} r={g.spacing * 0.45} fill="none" stroke={ex.last.seen ? "#1e7b34" : "#b3261e"} strokeWidth={0.35} data-testid="live-last" />
              )}
            </Frame>
          ) : (
            <p className="muted">O mapa aparece quando o exame do olho começar.</p>
          )}
          {ex && <p className="muted small">● em teste · · não iniciado · círculo: último estímulo ({ex.last ? `${fmt(ex.last.x)}°, ${fmt(ex.last.y)}°, ${ex.last.db === null ? "—" : fmt(ex.last.db + ex.hfaOffsetDb) + " dB"} ${ex.last.seen ? "visto" : "não visto"}` : "—"})</p>}
        </div>
        <div className="card">
          <h2 className="small" style={{ marginTop: 0 }}>Confiabilidade</h2>
          {ex ? (
            <dl className="live-rel">
              <Catch label="Perdas de fixação" c={ex.reliability.fixationLosses} />
              <Catch label="Falsos positivos" c={ex.reliability.falsePositives} />
              <Catch label="Falsos negativos" c={ex.reliability.falseNegatives} />
              {ex.reliability.gazeTracked && <><dt>Olhar descartado</dt><dd>{ex.reliability.gazeRejectedPresentations ?? 0}{ex.reliability.gazeMeanErrorDeg != null ? ` · erro médio ${fmt(ex.reliability.gazeMeanErrorDeg, 1)}°` : ""}</dd></>}
              <dt>Mancha cega</dt><dd>{ex.reliability.blindSpotFound ? "localizada" : "procurando"}{ex.reliability.blindSpotRemaps ? ` · relocalizada ${ex.reliability.blindSpotRemaps}×` : ""}</dd>
              <dt>Duração efetiva</dt><dd>{duration(ex.reliability.durationMs)}</dd>
            </dl>
          ) : <p className="muted">—</p>}
          <p className="muted small">Valores em dB equivalente HFA calculados no headset; o laudo completo é gerado na importação.</p>
        </div>
      </div>
    </div>
  );
}

function Catch({ label, c }: { label: string; c: LiveCatch & { alertLimit?: number } }) {
  const rate = c.trials ? c.errors / c.trials : null;
  const alert = rate !== null && c.alertLimit !== undefined && rate > c.alertLimit;
  return (<><dt>{label}</dt><dd className={alert ? "alert" : undefined}>{c.trials ? `${c.errors}/${c.trials}` : "—"}{alert ? " xx" : ""}</dd></>);
}
