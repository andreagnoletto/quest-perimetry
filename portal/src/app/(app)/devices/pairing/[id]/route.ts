import { NextResponse } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";

/** portal.json for the headset: this portal's URL (as the browser reached it) and the pairing code shown once. */
export async function GET(req: Request, { params }: { params: Promise<{ id: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id } = await params;
  const code = new URL(req.url).searchParams.get("code") ?? "";
  const pc = await db.pairingCode.findUnique({ where: { id } });
  if (!pc || pc.usedAt || !/^[2-9A-Z]{4}-[2-9A-Z]{4}$/.test(code)) return new NextResponse("não encontrado", { status: 404 });
  const host = req.headers.get("x-forwarded-host") ?? req.headers.get("host") ?? "localhost:3000";
  const proto = req.headers.get("x-forwarded-proto") ?? "http";
  await audit(user.id, "device.pairing.download", "pairingCode", id);
  const body = JSON.stringify({ url: `${proto}://${host}`, pairingCode: code }, null, 2) + "\n";
  return new NextResponse(body, { headers: { "content-type": "application/json; charset=utf-8", "content-disposition": 'attachment; filename="portal.json"', "cache-control": "no-store" } });
}
