"use server";

import { revalidatePath } from "next/cache";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { importFiles, type ImportOutcome } from "@/lib/importer";

export type ImportState = { outcomes?: ImportOutcome[]; error?: string } | undefined;

const MAX_FILES = 60;

export async function importAction(_: ImportState, form: FormData): Promise<ImportState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const files = form.getAll("files").filter((f): f is File => f instanceof File && f.size > 0);
  if (!files.length) return { error: "Escolha os arquivos exam_*.json, session_*.json e .hmac do headset." };
  if (files.length > MAX_FILES) return { error: `No máximo ${MAX_FILES} arquivos por vez.` };
  const deviceId = String(form.get("deviceId") ?? "") || null;
  const patientId = String(form.get("patientId") ?? "") || null;
  const uploaded = await Promise.all(files.map(async (f) => ({ name: f.name, bytes: Buffer.from(await f.arrayBuffer()) })));
  const outcomes = await importFiles(uploaded, { userId: user.id, deviceId, patientId });
  for (const o of outcomes.filter((x) => x.kind === "exam" && x.status === "imported")) {
    await audit(user.id, "exam.import", "exam", o.examDbId, `${o.file}; integrity=${o.integrity}; patient=${o.patientCode ?? "none"}`);
  }
  revalidatePath("/exams");
  return { outcomes };
}

export type LinkState = { error?: string; ok?: string } | undefined;

export async function linkExamAction(examId: string, _: LinkState, form: FormData): Promise<LinkState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const code = String(form.get("code") ?? "").trim().toUpperCase();
  const patient = code ? await db.patient.findUnique({ where: { code } }) : null;
  if (code && !patient) return { error: `Nenhum paciente com o código ${code}.` };
  await db.exam.update({ where: { id: examId }, data: { patientId: patient?.id ?? null } });
  await audit(user.id, patient ? "exam.link" : "exam.unlink", "exam", examId, patient?.code);
  revalidatePath(`/exams/${examId}`);
  return { ok: patient ? `Exame vinculado a ${patient.code}.` : "Vínculo removido." };
}
