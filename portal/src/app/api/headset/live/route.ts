import { db } from "@/lib/db";
import { auditDevice, authenticateDevice, readJson, syncError, syncJson } from "@/lib/headset-auth";
import { pendingCommands, publish } from "@/lib/live";
import { isLiveSnapshot, type LiveView } from "@/lib/live-types";

export const dynamic = "force-dynamic";

// POST /api/headset/live — one snapshot in, pending doctor commands out (docs/SYNC_API.md).
export async function POST(req: Request) {
  const device = await authenticateDevice(req);
  if (!device) return syncError("unauthorized", "token inválido ou revogado", 401);
  const body = await readJson(req, 4 * 1024 * 1024);
  if (!isLiveSnapshot(body)) return syncError("bad_request", "instantâneo inválido", 400);
  const snap = body;
  const now = new Date();
  const startedUtc = new Date(snap.startedUtc);
  if (Number.isNaN(startedUtc.getTime())) return syncError("bad_request", "startedUtc inválido", 400);

  const row = await db.liveSession.upsert({
    where: { sessionId: snap.sessionId },
    create: {
      sessionId: snap.sessionId, deviceId: device.id, patientRef: snap.patientRef ?? null, orderId: snap.orderId ?? null,
      startedUtc, lastSeenUtc: now, state: snap.state, finished: snap.finished, outcome: snap.outcome ?? null, seq: snap.seq, snapshot: snap as object,
    },
    update: {
      lastSeenUtc: now, state: snap.state, finished: snap.finished, outcome: snap.outcome ?? null, seq: snap.seq, snapshot: snap as object,
      patientRef: snap.patientRef ?? null, orderId: snap.orderId ?? null, deviceId: device.id,
    },
  });
  if (snap.ackCommandIds?.length) {
    await db.liveCommand.updateMany({ where: { liveSessionId: row.id, id: { in: snap.ackCommandIds.slice(0, 50) }, ackedAt: null }, data: { ackedAt: now } });
  }
  const commands = await pendingCommands(row.id);
  if (commands.length) await db.liveCommand.updateMany({ where: { id: { in: commands.map((c) => c.id) }, deliveredAt: null }, data: { deliveredAt: now } });

  const view: LiveView = {
    sessionId: snap.sessionId, snapshot: snap, receivedUtc: now.toISOString(),
    deviceLabel: device.label ?? device.model ?? device.serial, pendingCommands: commands,
  };
  publish(view);
  if (snap.seq <= 1 || snap.finished) await auditDevice(device, snap.finished ? "headset.live.end" : "headset.live.start", "liveSession", row.id, `${snap.patientRef ?? "no ref"} ${snap.outcome ?? snap.state}`, req);
  return syncJson({ commands });
}
