import { NextResponse } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";

/** The validated calibration, byte-for-byte, to copy to the headset as calibration.json. */
export async function GET(_req: Request, { params }: { params: Promise<{ calId: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { calId } = await params;
  const c = await db.calibration.findUnique({ where: { id: calId } });
  if (!c) return new NextResponse("não encontrado", { status: 404 });
  await audit(user.id, "calibration.download", "calibration", c.id);
  return new NextResponse(c.json, {
    headers: { "content-type": "application/json; charset=utf-8", "content-disposition": 'attachment; filename="calibration.json"', "cache-control": "no-store" },
  });
}
