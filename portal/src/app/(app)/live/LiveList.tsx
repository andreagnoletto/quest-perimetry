"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { dateTimeBr, eyeName } from "@/lib/format";
import { liveStateName } from "@/lib/live-types";

export interface LiveRow { sessionId: string; patientRef: string | null; state: string; finished: boolean; outcome: string | null; startedUtc: string; lastSeenUtc: string; active: boolean; eye: string | null; deviceLabel: string | null }

/** Polls the list every 3 s while the page is open. */
export function LiveList({ initial }: { initial: LiveRow[] }) {
  const [rows, setRows] = useState(initial);
  useEffect(() => {
    let alive = true;
    const tick = async () => {
      try {
        const r = await fetch("/api/live/sessions", { cache: "no-store" });
        if (r.ok && alive) setRows((await r.json()).sessions);
      } catch { /* offline: keep what we have */ }
    };
    const id = setInterval(tick, 3000);
    return () => { alive = false; clearInterval(id); };
  }, []);
  const active = rows.filter((r) => r.active), past = rows.filter((r) => !r.active);
  return (
    <>
      <h2>Em andamento</h2>
      {active.length === 0 ? <p className="muted" data-testid="live-none">Nenhum headset transmitindo agora.</p> : <Table rows={active} />}
      {past.length > 0 && <><h2>Encerradas recentemente</h2><Table rows={past} /></>}
    </>
  );
}

function Table({ rows }: { rows: LiveRow[] }) {
  return (
    <div className="table-wrap">
      <table>
        <thead><tr><th>Paciente</th><th>Headset</th><th>Estado</th><th>Olho</th><th>Início</th><th>Último envio</th></tr></thead>
        <tbody>
          {rows.map((r) => (
            <tr key={r.sessionId}>
              <td><Link href={`/live/${r.sessionId}`}>{r.patientRef ?? "sem pedido"}</Link></td>
              <td>{r.deviceLabel ?? "—"}</td>
              <td>{r.active ? <span className="live-dot" /> : null}{r.finished ? (r.outcome === "aborted" ? "Interrompido" : "Concluído") : liveStateName[r.state] ?? r.state}</td>
              <td>{r.eye ? eyeName(r.eye) : "—"}</td>
              <td>{dateTimeBr(r.startedUtc)}</td>
              <td>{dateTimeBr(r.lastSeenUtc)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
