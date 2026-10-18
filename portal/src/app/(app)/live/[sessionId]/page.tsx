import { notFound } from "next/navigation";
import { requireUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { loadView } from "@/lib/live";
import { presets } from "@/lib/headset";
import { view as patientView } from "@/lib/patients";
import { LivePanel } from "./LivePanel";

export const dynamic = "force-dynamic";

export default async function LiveSessionPage({ params }: { params: Promise<{ sessionId: string }> }) {
  const user = await requireUser();
  const { sessionId } = await params;
  const v = await loadView(sessionId);
  if (!v) notFound();
  const row = v.snapshot.patientRef ? await db.patient.findUnique({ where: { code: v.snapshot.patientRef } }) : null;
  const patient = row ? patientView(row) : null;
  await audit(user.id, "live.view", "liveSession", sessionId, `${v.snapshot.patientRef ?? "no ref"}${patient ? "; patient " + patient.id : ""}`);
  const preset = presets().find((p) => p.id === v.snapshot.presetId);
  return (
    <>
      <h1>Ao vivo · {patient?.name ?? v.snapshot.patientRef ?? "sem pedido"} <span className="muted small">{v.snapshot.patientRef ?? ""}</span></h1>
      <LivePanel initial={v} patientName={patient?.name ?? null} patientId={patient?.id ?? null} presetLabel={preset?.label ?? v.snapshot.presetId ?? "—"} />
    </>
  );
}
