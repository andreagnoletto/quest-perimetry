import Link from "next/link";
import { requireUser } from "@/lib/auth";
import { db } from "@/lib/db";
import { ExamTable } from "@/components/ExamTable";
import { listSessions } from "@/lib/live";

export default async function Home({ searchParams }: { searchParams: Promise<{ forbidden?: string }> }) {
  const user = await requireUser();
  const sp = await searchParams;
  const live = (await listSessions()).filter((s) => s.active);
  const [patients, exams, unlinked, recent] = await Promise.all([
    db.patient.count(),
    db.exam.count(),
    db.exam.count({ where: { patientId: null } }),
    db.exam.findMany({ orderBy: { importedAt: "desc" }, take: 10, include: { patient: { select: { code: true } } } }),
  ]);
  return (
    <>
      {sp.forbidden && <div className="notice error">Seu perfil não tem acesso a essa página.</div>}
      <h1>Olá, {user.name}</h1>
      <div className="actions">
        <Link className="button" href="/patients/new">Novo paciente</Link>
        <Link className="button secondary" href="/exams/import">Importar exames do headset</Link>
      </div>
      {live.length > 0 && <div className="notice"><span className="live-dot" />{live.length === 1 ? "1 exame em andamento" : `${live.length} exames em andamento`} no headset · <Link href="/live">acompanhar ao vivo</Link></div>}
      <p className="muted">{patients} pacientes · {exams} exames · {unlinked > 0 ? <Link href="/exams?unlinked=1">{unlinked} exames sem paciente</Link> : "todos os exames vinculados"}</p>
      <h2>Últimos exames importados</h2>
      <ExamTable exams={recent} />
    </>
  );
}
