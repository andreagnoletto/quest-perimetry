import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { loadExam } from "@/lib/exams";
import { dateTimeBr } from "@/lib/format";
import { SingleFieldReport } from "@/components/SingleFieldReport";

export const dynamic = "force-dynamic";

/** Bare report for printing (the PDF route opens this page in headless Chromium). */
export default async function PrintExam({ params }: { params: Promise<{ id: string }> }) {
  const user = await requireUser();
  const { id } = await params;
  const x = await loadExam(id, user.id, "exam.print");
  if (!x) notFound();
  return <SingleFieldReport report={x.report} patient={x.patient} device={x.device} integrity={x.exam.integrity} footerNote={`impresso por ${user.name} em ${dateTimeBr(new Date())}`} />;
}
