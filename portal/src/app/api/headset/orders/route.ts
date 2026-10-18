import { db } from "@/lib/db";
import { auditDevice, authenticateDevice, syncError, syncJson } from "@/lib/headset-auth";

export const dynamic = "force-dynamic";

// GET /api/headset/orders — pending orders sent to this headset (pseudonymous code only).
export async function GET(req: Request) {
  const device = await authenticateDevice(req);
  if (!device) return syncError("unauthorized", "token inválido ou revogado", 401);
  const rows = await db.examOrder.findMany({
    where: { deviceId: device.id, fulfilledAt: null, cancelledAt: null },
    orderBy: { createdAt: "desc" },
    take: 20,
    include: { patient: { select: { code: true } } },
  });
  await auditDevice(device, "headset.orders", "device", device.id, `${rows.length} pending`, req);
  return syncJson({
    orders: rows.map((o) => ({
      id: o.id,
      patientRef: o.patient.code,
      ageYears: Math.round(o.ageYears * 10) / 10,
      refractiveCorrection: o.refraction ?? null,
      presetId: o.presetId,
      seed: o.seed ? Number(o.seed) : null,
      createdUtc: o.createdAt.toISOString(),
    })),
  });
}
