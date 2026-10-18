import Link from "next/link";
import { notFound, redirect } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { view } from "@/lib/patients";
import { anonymizePatientAction } from "../../actions";
import { AnonymizeForm } from "./AnonymizeForm";

export default async function AnonymizePatient({ params }: { params: Promise<{ id: string }> }) {
  const user = await requireUser(["admin", "doctor"]);
  const { id } = await params;
  const row = await db.patient.findUnique({ where: { id }, include: { _count: { select: { exams: true, orders: { where: { fulfilledAt: null, cancelledAt: null } } } } } });
  if (!row) notFound();
  if (row.anonymizedAt) redirect(`/patients/${id}`);
  const p = view(row);
  await audit(user.id, "patient.view", "patient", id, "anonymize form");
  return (
    <>
      <h1>Anonimizar {p.name}</h1>
      <div className="card">
        <p>A anonimização atende ao pedido de eliminação do titular (LGPD, art. 18 VI) sem destruir o registro clínico:</p>
        <ul>
          <li>nome, ID interno, nascimento, sexo, refração, diagnóstico e observação do consentimento são <b>apagados definitivamente</b> (não há como desfazer);</li>
          <li>os {row._count.exams} exame(s), laudos e arquivos brutos do headset continuam sob o código <b>{p.code}</b>, sem ligação com a pessoa, para auditoria e reanálise;</li>
          <li>a autorização de pesquisa é retirada e o paciente sai da exportação de pesquisa;</li>
          <li>{row._count.orders} pedido(s) de exame em aberto são cancelados; não é mais possível editar ou pedir exames;</li>
          <li>a operação fica no log de acesso com o seu usuário.</li>
        </ul>
        <p className="muted small">Antes de anonimizar, se o paciente pediu a cópia dos dados, use <a href={`/patients/${id}/export`} download>Exportar dados do paciente (LGPD)</a> na página do paciente.</p>
      </div>
      <AnonymizeForm action={anonymizePatientAction.bind(null, id)} code={p.code} />
      <p><Link href={`/patients/${id}`}>← voltar sem anonimizar</Link></p>
    </>
  );
}
