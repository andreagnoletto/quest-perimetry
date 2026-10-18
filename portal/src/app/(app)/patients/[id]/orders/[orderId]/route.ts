import { NextResponse } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { examOrderFile } from "@/lib/headset";

export async function GET(_req: Request, { params }: { params: Promise<{ id: string; orderId: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id, orderId } = await params;
  const order = await db.examOrder.findFirst({ where: { id: orderId, patientId: id }, include: { patient: { select: { code: true } } } });
  if (!order) return new NextResponse("não encontrado", { status: 404 });
  await audit(user.id, "order.download", "examOrder", order.id, order.patient.code);
  const body = JSON.stringify(examOrderFile({
    code: order.patient.code, ageYears: order.ageYears, refraction: order.refraction, presetId: order.presetId, seed: order.seed,
    gridId: order.gridId, fixationMode: order.fixationMode, fovealThreshold: order.fovealThreshold, responseMode: order.responseMode,
  }), null, 2);
  return new NextResponse(body + "\n", {
    headers: { "content-type": "application/json; charset=utf-8", "content-disposition": 'attachment; filename="exam-order.json"', "cache-control": "no-store" },
  });
}
