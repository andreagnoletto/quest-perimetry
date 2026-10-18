"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { activateNormative, buildNormativeSet, deactivateNormative, reanalyzeGrid } from "@/lib/normative";

// Phase 6: building, activating and applying the clinic's own normative. Doctors and admins only; every
// step is audited because it changes how every later report of the grid is read.

const ROLES = ["admin", "doctor"] as const;

function back(gridId: string, msg: string, error = false): never {
  const q = new URLSearchParams({ grid: gridId, [error ? "error" : "msg"]: msg });
  redirect(`/normative?${q}`);
}

export async function buildNormativeAction(gridId: string, _form: FormData) {
  const user = await requireUser([...ROLES]);
  let outcome: { ok: true; label: string; status: string; eyes: number } | { ok: false; error: string };
  try {
    const { set, summary } = await buildNormativeSet(gridId, user.id);
    outcome = { ok: true, label: set.label, status: set.status, eyes: summary.included };
  } catch (e) {
    outcome = { ok: false, error: e instanceof Error ? e.message : String(e) };
  }
  if (!outcome.ok) {
    await audit(user.id, "normative.build", "normative", undefined, `${gridId}; FALHOU: ${outcome.error.slice(0, 300)}`);
    back(gridId, `Não foi possível construir o normativo: ${outcome.error}`, true);
  }
  await audit(user.id, "normative.build", "normative", outcome.label, `${gridId}; ${outcome.status}; ${outcome.eyes} olhos`);
  back(gridId, `Normativo ${outcome.label} construído (${outcome.status}, ${outcome.eyes} olhos). Ele só passa a valer depois de ativado.`);
}

export async function activateNormativeAction(setId: string, _form: FormData) {
  const user = await requireUser([...ROLES]);
  const set = await activateNormative(setId, user.id);
  await audit(user.id, "normative.activate", "normative", set.id, `${set.gridId}; ${set.label}; ${set.status}; ${set.populationEyes} olhos`);
  revalidatePath("/normative");
  back(set.gridId, `${set.label} é agora o normativo ativo da grade ${set.gridId}. Os exames já importados mantêm a análise anterior até serem reanalisados.`);
}

export async function deactivateNormativeAction(setId: string, _form: FormData) {
  const user = await requireUser([...ROLES]);
  const set = await deactivateNormative(setId);
  await audit(user.id, "normative.deactivate", "normative", set.id, `${set.gridId}; ${set.label}`);
  revalidatePath("/normative");
  back(set.gridId, `${set.label} desativado: novas análises da grade ${set.gridId} voltam ao normativo provisório sintético.`);
}

export async function reanalyzeGridAction(gridId: string, _form: FormData) {
  const user = await requireUser([...ROLES]);
  const active = await db.normativeSet.findFirst({ where: { gridId, activatedAt: { not: null } }, select: { label: true } });
  const r = await reanalyzeGrid(gridId);
  await audit(user.id, "normative.reanalyze", "normative", active?.label ?? "synthetic", `${gridId}; ${r.reanalyzed}/${r.total} exames reanalisados${r.failed.length ? `; falhas: ${r.failed.map((f) => f.examId).join(", ").slice(0, 300)}` : ""}`);
  revalidatePath("/normative");
  revalidatePath("/exams");
  const failed = r.failed.length ? ` ${r.failed.length} falharam (${r.failed.map((f) => f.examId).slice(0, 5).join(", ")}).` : "";
  back(gridId, `${r.reanalyzed} de ${r.total} exames da grade ${gridId} reanalisados com ${active ? `o normativo ${active.label}` : "o normativo provisório sintético"}.${failed}`, r.failed.length > 0);
}
