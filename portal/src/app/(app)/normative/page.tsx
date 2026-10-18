import Link from "next/link";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { dateBr, dateTimeBr, fmt } from "@/lib/format";
import { gridIds } from "@/lib/headset";
import { cohortStats, exclusionReasonName } from "@/lib/normative";
import { PerimetryError } from "@/lib/perimetry";
import { activateNormativeAction, buildNormativeAction, deactivateNormativeAction, reanalyzeGridAction } from "./actions";

export const dynamic = "force-dynamic";

// Phase 6: the clinic's own normative. The page shows what /core would include or exclude from the volunteers'
// exams (`perimetry normative stats`), builds the database (`normative build`) and activates one set per grid.
// Nothing here applies a clinical rule: counts, reasons, bands, status and warnings all come back from /core.

const MIN_EYES_TO_BUILD = 30; // informational only: the real rule is in /core (Build refuses fewer)

export default async function NormativePage({ searchParams }: { searchParams: Promise<{ grid?: string; msg?: string; error?: string }> }) {
  const user = await requireUser(["admin", "doctor"]);
  const q = await searchParams;
  const grids = gridIds();
  const grid = grids.includes(q.grid ?? "") ? q.grid! : grids[0];
  await audit(user.id, "normative.view", "normative", grid);

  let data: Awaited<ReturnType<typeof cohortStats>> | null = null;
  let statsError: string | null = null;
  try { data = await cohortStats(grid); } catch (e) { statsError = e instanceof PerimetryError ? e.message : "não foi possível rodar a estatística da coorte"; }

  const sets = await db.normativeSet.findMany({ where: { gridId: grid }, orderBy: { builtAt: "desc" }, include: { _count: { select: { exams: true } } } });
  const builders = new Map((await db.user.findMany({ where: { id: { in: sets.map((n) => n.builtById) } }, select: { id: true, name: true } })).map((u) => [u.id, u.name]));
  const active = sets.find((s) => s.activatedAt);
  const examsOfGrid = await db.exam.count({ where: { gridId: grid } });
  const examsOnActive = active ? active._count.exams : await db.exam.count({ where: { gridId: grid, normativeSetId: null } });
  const s = data?.stats;
  const links = data?.cohort.examLinks ?? {};

  return (
    <>
      <h1>Normativo próprio da clínica</h1>
      {q.msg && <div className="notice">{q.msg}</div>}
      {q.error && <div className="notice error">{q.error}</div>}

      <div className="actions" aria-label="grade">
        {grids.map((g) => <Link key={g} className={`button ${g === grid ? "" : "secondary"}`} href={`/normative?grid=${g}`}>{g}</Link>)}
      </div>

      <div className="card">
        <p className="muted small" style={{ marginTop: 0 }}>
          Entram os exames da grade {grid} dos pacientes marcados como <b>voluntário normal</b>. As regras de inclusão (idade, confiabilidade, efeito de
          aprendizado, um exame por olho) e toda a estatística vêm do <code>/core</code> (<code>config/normative/cohort.json</code>); o portal só reúne os
          candidatos e mostra o resultado. A coorte sai pseudonimizada (ID de estudo S-…), sem nome, nascimento ou código do headset.
          Padrão proposto da Fase 6, aguardando confirmação do product owner.
        </p>
        {statsError ? <p className="alert">{statsError}</p> : s && data && (
          <>
            <div className="form-grid">
              <div><span className="muted">Voluntários</span><br />{data.cohort.volunteers}{data.cohort.withoutResearchConsent ? <span className="muted small"> ({data.cohort.withoutResearchConsent} sem autorização de pesquisa; o normativo é uso interno pseudonimizado)</span> : null}</div>
              <div><span className="muted">Exames candidatos</span><br />{s.candidates}</div>
              <div><span className="muted">Olhos elegíveis</span><br /><b data-included>{s.included}</b> <span className="muted small">({s.eyesOd} OD, {s.eyesOs} OS, {s.subjects} sujeitos, fóvea em {s.eyesWithFovea})</span></div>
              <div><span className="muted">Faixa etária</span><br />{s.ageRange[0] !== null && s.ageRange[1] !== null ? `${fmt(s.ageRange[0], 0)}–${fmt(s.ageRange[1], 0)} anos` : "—"}</div>
              <div><span className="muted">Status se construído agora</span><br /><b data-status={s.status}>{s.status}</b><br /><span className="muted small">{s.statusReason}</span></div>
            </div>
            {s.warnings.length > 0 && <ul className="small" style={{ marginBottom: 0 }}>{s.warnings.map((w) => <li key={w} className="alert" style={{ fontWeight: 400 }}>{w}</li>)}</ul>}
            <div className="actions">
              <a className="button secondary" href={`/normative/export?grid=${grid}`}>Exportar coorte (JSON)</a>
              <form action={buildNormativeAction.bind(null, grid)} style={{ display: "inline" }}>
                <button type="submit" disabled={s.included < MIN_EYES_TO_BUILD} title={s.included < MIN_EYES_TO_BUILD ? `são necessários pelo menos ${MIN_EYES_TO_BUILD} olhos elegíveis` : undefined}>Construir normativo</button>
              </form>
            </div>
          </>
        )}
      </div>

      {s && s.eyesByBand.length > 0 && (
        <>
          <h2>Olhos elegíveis por faixa etária</h2>
          <div className="table-wrap card" style={{ padding: 0 }}>
            <table data-table="bands">
              <thead><tr><th>Faixa (anos)</th><th className="num">Olhos</th><th className="num">Sujeitos</th><th className="num">Idade média</th><th className="num">Sensibilidade média (dB equiv. HFA)</th><th className="num">DP entre olhos</th><th className="num">Com fóvea</th></tr></thead>
              <tbody>
                {s.eyesByBand.map((b) => (
                  <tr key={b.Band} className={b.Eyes > 0 && b.Eyes < 10 ? "alert" : undefined} style={{ fontWeight: 400 }}>
                    <td>{b.Band}</td><td className="num">{b.Eyes}</td><td className="num">{b.Subjects}</td><td className="num">{b.Eyes ? fmt(b.MeanAge, 1) : "—"}</td>
                    <td className="num">{fmt(b.MeanSensitivityHfaDb, 2)}</td><td className="num">{fmt(b.SdSensitivityHfaDb, 2)}</td><td className="num">{b.EyesWithFovea}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </>
      )}

      {s && s.excluded.length > 0 && (
        <>
          <h2>Exames excluídos ({s.excluded.length})</h2>
          <div className="table-wrap card" style={{ padding: 0 }}>
            <table data-table="excluded">
              <thead><tr><th>Sujeito</th><th>Olho</th><th>Exame</th><th>Motivo</th><th>Detalhe</th></tr></thead>
              <tbody>
                {s.excluded.map((x) => (
                  <tr key={`${x.ExamId}-${x.Eye}`}>
                    <td className="muted small">{x.SubjectId}</td><td>{x.Eye}</td>
                    <td>{links[x.ExamId] ? <Link href={`/exams/${links[x.ExamId]}`}>{x.ExamId}</Link> : x.ExamId}</td>
                    <td>{exclusionReasonName[x.Reason] ?? x.Reason}</td><td className="muted small">{x.Detail ?? ""}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
          <p className="muted small">Resumo: {s.exclusionsByReason.map((r) => `${exclusionReasonName[r.Reason] ?? r.Reason} ${r.Count}`).join(" · ")}</p>
        </>
      )}

      <h2>Normativos construídos para {grid}</h2>
      <div className="card">
        {sets.length === 0 ? <p className="muted">Nenhum ainda. Enquanto não houver um normativo ativo, os exames da grade {grid} são analisados com o normativo provisório sintético.</p> : (
          <div className="table-wrap">
            <table data-table="sets">
              <thead><tr><th>Normativo</th><th>Status</th><th className="num">Olhos</th><th>Faixa etária</th><th>Construído</th><th className="num">Exames analisados com ele</th><th>Situação</th><th></th></tr></thead>
              <tbody>
                {sets.map((n) => (
                  <tr key={n.id} data-set={n.label}>
                    <td><b>{n.label}</b><br /><span className="muted small">coorte {n.cohortHash.slice(0, 12)}</span></td>
                    <td data-status={n.status}>{n.status}<br /><span className="muted small">{n.statusReason}</span></td>
                    <td className="num">{n.populationEyes}</td>
                    <td>{n.ageMin !== null && n.ageMax !== null ? `${fmt(n.ageMin, 0)}–${fmt(n.ageMax, 0)} anos` : "—"}</td>
                    <td>{dateTimeBr(n.builtAt)}<br /><span className="muted small">por {builders.get(n.builtById) ?? "—"}</span></td>
                    <td className="num">{n._count.exams}</td>
                    <td>{n.activatedAt ? <span className="ok">ativo desde {dateBr(n.activatedAt)}</span> : <span className="muted">inativo</span>}</td>
                    <td>
                      {n.activatedAt
                        ? <form action={deactivateNormativeAction.bind(null, n.id)}><button className="secondary" type="submit">Desativar</button></form>
                        : <form action={activateNormativeAction.bind(null, n.id)}><button type="submit">Ativar</button></form>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="actions" style={{ alignItems: "center" }}>
          <form action={reanalyzeGridAction.bind(null, grid)} style={{ display: "inline" }}>
            <button className="secondary" type="submit" disabled={examsOfGrid === 0}>Reanalisar todos os exames desta grade</button>
          </form>
          <span className="muted small">
            {examsOfGrid} exames da grade {grid}; {examsOnActive} já analisados com {active ? `o normativo ativo (${active.label})` : "o normativo provisório sintético"}.
            Reanalisar reprocessa cada arquivo original com o <code>/core</code> e o normativo ativo agora; o relatório de cada exame registra qual normativo foi usado.
          </span>
        </div>
      </div>
    </>
  );
}
