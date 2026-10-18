import { requireUser } from "@/lib/auth";
import { db } from "@/lib/db";

export default async function Research() {
  await requireUser(["admin"]);
  const [consented, total] = await Promise.all([db.patient.count({ where: { researchConsent: true } }), db.patient.count()]);
  return (
    <>
      <h1>Exportação para pesquisa</h1>
      <div className="card">
        <p>{consented} de {total} pacientes autorizaram o uso em pesquisa. Só eles entram na exportação.</p>
        <p className="muted">Os arquivos não trazem nome, ID interno, data de nascimento, código do headset, diagnóstico nem datas. Cada paciente recebe um ID de estudo (S-…) que só o servidor consegue relacionar; o tempo vai como dias desde o primeiro exame e a idade em anos completos. Exames interrompidos ficam de fora. Cada exportação fica no log de acesso.</p>
        <div className="actions">
          <a className="button" href="/research/export?type=exams">Exames (CSV)</a>
          <a className="button secondary" href="/research/export?type=points">Pontos (CSV)</a>
        </div>
      </div>
    </>
  );
}
