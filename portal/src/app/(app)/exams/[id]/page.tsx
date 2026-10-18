import Link from "next/link";
import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { loadExam } from "@/lib/exams";
import { dateTimeBr } from "@/lib/format";
import { SingleFieldReport } from "@/components/SingleFieldReport";
import { normativeLine } from "@/lib/report-format";
import { linkExamAction, reanalyzeExamAction } from "../actions";
import { LinkForm } from "./LinkForm";

export default async function ExamPage({ params }: { params: Promise<{ id: string }> }) {
  const user = await requireUser();
  const { id } = await params;
  const x = await loadExam(id, user.id, "exam.view");
  if (!x) notFound();
  const { exam, report, patient, device } = x;
  return (
    <>
      <div className="no-print">
        <p>{patient ? <Link href={`/patients/${patient.id}`}>← {patient.name}</Link> : <Link href="/exams?unlinked=1">← exames sem paciente</Link>}</p>
        <div className="actions">
          <a className="button" href={`/exams/${id}/pdf`}>PDF (A4)</a>
          <a className="button secondary" href={`/exams/${id}/export?format=csv`}>Pontos (CSV)</a>
          <a className="button secondary" href={`/exams/${id}/export?format=json`}>Análise (JSON)</a>
          <a className="button secondary" href={`/exams/${id}/export?format=raw`}>Arquivo original do headset</a>
        </div>
        <div className="card">
          <LinkForm action={linkExamAction.bind(null, id)} code={patient?.code ?? null} />
          <p className="muted small">
            Importado em {dateTimeBr(exam.importedAt)}. Config do headset {exam.configHash?.slice(0, 12) ?? "—"}, análise com {exam.analyzedConfig.slice(0, 12)}{normativeLine(report) ? `, ${normativeLine(report)}` : ""}.
            {user.role !== "operator" && <form action={reanalyzeExamAction.bind(null, id)} style={{ display: "inline" }}> <button className="link" type="submit">Reanalisar</button> <span>com o normativo ativo hoje (o arquivo original é reprocessado pelo /core).</span></form>}
          </p>
        </div>
      </div>
      <SingleFieldReport report={report} patient={patient} device={device} integrity={exam.integrity} />
    </>
  );
}
