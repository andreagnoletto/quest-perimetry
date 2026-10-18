import { requireUser } from "@/lib/auth";
import { db } from "@/lib/db";
import { dateTimeBr } from "@/lib/format";

export default async function AuditPage({ searchParams }: { searchParams: Promise<{ entity?: string; id?: string }> }) {
  await requireUser(["admin"]);
  const sp = await searchParams;
  const where = { ...(sp.entity ? { entityType: sp.entity } : {}), ...(sp.id ? { entityId: sp.id } : {}) };
  const rows = await db.accessLog.findMany({ where, orderBy: { at: "desc" }, take: 500, include: { user: { select: { name: true, email: true } } } });
  return (
    <>
      <h1>Log de acesso</h1>
      <form className="row card">
        <label>Tipo
          <select name="entity" defaultValue={sp.entity ?? ""}>
            <option value="">todos</option>
            {["patient", "exam", "examOrder", "device", "calibration", "research", "user"].map((t) => <option key={t} value={t}>{t}</option>)}
          </select>
        </label>
        <label>ID do registro<input name="id" defaultValue={sp.id ?? ""} /></label>
        <button type="submit">Filtrar</button>
      </form>
      <p className="muted small">Últimos {rows.length} registros. O log é só de inclusão; nada aqui altera dados.</p>
      <div className="table-wrap">
        <table>
          <thead><tr><th>Quando</th><th>Usuário</th><th>Ação</th><th>Registro</th><th>Detalhe</th><th>IP</th></tr></thead>
          <tbody>
            {rows.map((r) => (
              <tr key={r.id}>
                <td>{dateTimeBr(r.at)}</td>
                <td>{r.user ? `${r.user.name} (${r.user.email})` : "—"}</td>
                <td>{r.action}</td>
                <td className="small">{r.entityType ?? ""} {r.entityId ?? ""}</td>
                <td className="small">{r.detail ?? ""}</td>
                <td className="small">{r.ip ?? ""}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
