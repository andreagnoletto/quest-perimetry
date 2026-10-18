import Link from "next/link";
import { dateTimeBr, eyeName, fmt, strategyName } from "@/lib/format";

type Row = { id: string; startedUtc: Date; eye: string; gridId: string; strategy: string; md: number | null; vfi: number | null; aborted: boolean; integrity: string; patient: { code: string } | null };

export function ExamTable({ exams }: { exams: Row[] }) {
  if (!exams.length) return <p className="muted">Nenhum exame.</p>;
  return (
    <div className="table-wrap">
      <table>
        <thead><tr><th>Data</th><th>Paciente</th><th>Olho</th><th>Programa</th><th className="num">MD</th><th className="num">VFI</th><th>Integridade</th></tr></thead>
        <tbody>
          {exams.map((e) => (
            <tr key={e.id}>
              <td><Link href={`/exams/${e.id}`}>{dateTimeBr(e.startedUtc)}</Link></td>
              <td>{e.patient?.code ?? <span className="alert">sem paciente</span>}</td>
              <td>{eyeName(e.eye)}</td>
              <td>{e.gridId} {strategyName[e.strategy] ?? e.strategy}{e.aborted ? <span className="alert"> (interrompido)</span> : null}</td>
              <td className="num">{fmt(e.md, 2)}</td>
              <td className="num">{e.vfi === null ? "—" : fmt(e.vfi) + "%"}</td>
              <td><IntegrityBadge value={e.integrity} /></td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

export function IntegrityBadge({ value }: { value: string }) {
  const label = value === "verified" ? "verificada" : value === "failed" ? "FALHOU" : "não verificada";
  return <span className={`badge ${value}`}>{label}</span>;
}
