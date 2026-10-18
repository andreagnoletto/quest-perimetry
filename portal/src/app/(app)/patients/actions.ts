"use server";

import { redirect } from "next/navigation";
import { revalidatePath } from "next/cache";
import { z } from "zod";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { ageAt, createPatient, updatePatient, view } from "@/lib/patients";
import { presets } from "@/lib/headset";

export type FormState = { error?: string; ok?: string } | undefined;

const opt = z.string().trim().max(500).optional().transform((v) => (v ? v : null));
const patientSchema = z.object({
  internalId: z.string().trim().min(1, "Informe o ID interno (prontuário)").max(64),
  name: z.string().trim().min(2, "Informe o nome").max(200),
  birthDate: z.string().regex(/^\d{4}-\d{2}-\d{2}$/, "Data de nascimento inválida").refine((d) => {
    const t = Date.parse(d + "T00:00:00Z");
    return Number.isFinite(t) && t < Date.now() && t > Date.parse("1900-01-01");
  }, "Data de nascimento inválida"),
  sex: opt, refraction: opt, diagnosis: opt,
});

const firstError = (e: z.ZodError) => e.issues[0]?.message ?? "Dados inválidos";

export async function createPatientAction(_: FormState, form: FormData): Promise<FormState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const parsed = patientSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { error: firstError(parsed.error) };
  if (form.get("consent") !== "on") return { error: "Registre o consentimento do paciente para o exame e o armazenamento dos dados." };
  let id: string;
  try {
    const p = await createPatient({
      ...parsed.data,
      consent: true,
      consentNote: String(form.get("consentNote") ?? "").trim() || null,
      researchConsent: form.get("researchConsent") === "on",
    }, user.id);
    id = p.id;
    await audit(user.id, "patient.create", "patient", p.id, `consent; research=${p.researchConsent}`);
  } catch (e) {
    return { error: e instanceof Error && e.message.startsWith("Já existe") ? e.message : "Não foi possível salvar o paciente." };
  }
  redirect(`/patients/${id}`);
}

export async function updatePatientAction(id: string, _: FormState, form: FormData): Promise<FormState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const parsed = patientSchema.safeParse(Object.fromEntries(form));
  if (!parsed.success) return { error: firstError(parsed.error) };
  try {
    await updatePatient(id, parsed.data);
  } catch (e) {
    const code = (e as { code?: string }).code;
    return { error: code === "P2002" ? "Já existe um paciente com esse ID interno" : "Não foi possível salvar." };
  }
  const researchConsent = form.get("researchConsent") === "on";
  await db.patient.update({ where: { id }, data: { researchConsent } });
  await audit(user.id, "patient.update", "patient", id, `research=${researchConsent}`);
  redirect(`/patients/${id}`);
}

export async function createOrderAction(patientId: string, _: FormState, form: FormData): Promise<FormState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  const presetId = String(form.get("presetId") ?? "");
  if (!presets().some((p) => p.id === presetId)) return { error: "Exame inválido." };
  const seedRaw = String(form.get("seed") ?? "").trim();
  if (seedRaw && !/^\d{1,9}$/.test(seedRaw)) return { error: "A semente deve ser um número inteiro." };
  const row = await db.patient.findUnique({ where: { id: patientId } });
  if (!row) return { error: "Paciente não encontrado." };
  const p = view(row);
  const refraction = String(form.get("refraction") ?? "").trim() || p.refraction;
  const order = await db.examOrder.create({
    data: { patientId, presetId, ageYears: ageAt(p.birthDate, new Date()), refraction, seed: seedRaw || null, createdById: user.id },
  });
  await audit(user.id, "order.create", "examOrder", order.id, `patient ${row.code}, ${presetId}`);
  revalidatePath(`/patients/${patientId}`);
  return { ok: "Pedido criado. Baixe o arquivo e copie para o headset." };
}
