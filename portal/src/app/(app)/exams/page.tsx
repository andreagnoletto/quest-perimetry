import Link from "next/link";
import { requireUser } from "@/lib/auth";
import { db } from "@/lib/db";
import { ExamTable } from "@/components/ExamTable";

export default async function Exams({ searchParams }: { searchParams: Promise<{ unlinked?: string }> }) {
  await requireUser();
  const unlinked = (await searchParams).unlinked === "1";
  const exams = await db.exam.findMany({
    where: unlinked ? { patientId: null } : {},
    orderBy: { startedUtc: "desc" }, take: 200,
    include: { patient: { select: { code: true } } },
  });
  return (
    <>
      <h1>Exames{unlinked ? " sem paciente" : ""}</h1>
      <p>{unlinked ? <Link href="/exams">Ver todos</Link> : <Link href="/exams?unlinked=1">Só os sem paciente</Link>}</p>
      <ExamTable exams={exams} />
    </>
  );
}
