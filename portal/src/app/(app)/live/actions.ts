"use server";

import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { cached, pendingCommands, publish } from "@/lib/live";
import type { LiveCommandType } from "@/lib/live-types";

export type CommandState = { error?: string; ok?: string } | undefined;

const TYPES: LiveCommandType[] = ["pause", "resume", "abort"];
const label: Record<LiveCommandType, string> = { pause: "Pausa", resume: "Retomada", abort: "Encerramento" };

/** Doctor's pause / resume / abort for a running headset session; delivered on the headset's next live post. */
export async function liveCommandAction(sessionId: string, type: LiveCommandType): Promise<CommandState> {
  const user = await requireUser(["admin", "doctor", "operator"]);
  if (!TYPES.includes(type)) return { error: "Comando inválido." };
  const row = await db.liveSession.findUnique({ where: { sessionId } });
  if (!row) return { error: "Sessão não encontrada." };
  if (row.finished) return { error: "A sessão já terminou." };
  if (Date.now() - row.lastSeenUtc.getTime() > 30_000) return { error: "O headset não envia dados há mais de 30 s; o comando ficará pendente até ele voltar." };
  const cmd = await db.liveCommand.create({ data: { liveSessionId: row.id, type, issuedById: user.id } });
  await audit(user.id, "live.command", "liveSession", row.id, `${type} ${row.patientRef ?? ""}`.trim());
  const view = cached(sessionId);
  if (view) publish({ ...view, pendingCommands: await pendingCommands(row.id) });
  return { ok: `${label[type]} enviada ao headset (${cmd.id.slice(-6)}).` };
}
