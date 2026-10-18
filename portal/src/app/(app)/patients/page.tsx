import Link from "next/link";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { dateBr } from "@/lib/format";
import { findPatients, view } from "@/lib/patients";

export default async function Patients({ searchParams }: { searchParams: Promise<{ q?: string }> }) {
  const user = await requireUser();
  const q = ((await searchParams).q ?? "").slice(0, 200);
  const rows = await findPatients(q);
  const counts = await db.exam.groupBy({ by: ["patientId"], where: { patientId: { in: rows.map((r) => r.id) } }, _count: true, _max: { startedUtc: true } });
  const byId = new Map(counts.map((c) => [c.patientId, c]));
  const list = rows.map(view);
  // The search text may be a name, so only the fact that a search happened is logged.
  await audit(user.id, "patient.list", "patient", undefined, `${list.length} pacientes${q ? ", com busca" : ""}`);
  return (
    <>
      <h1>Pacientes</h1>
      <form className="row card" role="search">
        <label style={{ flex: 1, minWidth: 220 }}>Buscar por nome completo, ID interno ou código
          <input name="q" defaultValue={q} placeholder="Maria da Silva, 12345 ou P-7KQ2M9" />
        </label>
        <button type="submit">Buscar</button>
        <Link className="button secondary" href="/patients/new">Novo paciente</Link>
      </form>
      {q && <p className="muted small">A busca por nome é exata (nome completo, sem diferenciar acentos e maiúsculas), porque os nomes ficam criptografados. Pacientes anonimizados só aparecem pelo código.</p>}
      {list.length === 0 ? <p className="muted">Nenhum paciente encontrado.</p> : (
        <div className="table-wrap">
          <table>
            <thead><tr><th>Nome</th><th>ID interno</th><th>Código</th><th>Nascimento</th><th className="num">Exames</th><th>Último exame</th></tr></thead>
            <tbody>
              {list.map((p) => {
                const c = byId.get(p.id);
                return (
                  <tr key={p.id}>
                    <td><Link href={`/patients/${p.id}`}>{p.name}</Link>{p.anonymizedAt && <span className="badge" style={{ marginLeft: 6 }}>anonimizado</span>}</td>
                    <td>{p.internalId}</td>
                    <td>{p.code}</td>
                    <td>{p.birthDate ? dateBr(p.birthDate + "T12:00:00Z") : "—"}</td>
                    <td className="num">{c?._count ?? 0}</td>
                    <td>{c?._max.startedUtc ? dateBr(c._max.startedUtc) : "—"}</td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}
    </>
  );
}
