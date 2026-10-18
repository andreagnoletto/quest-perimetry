import { NextResponse, type NextRequest } from "next/server";
import { currentUser, SESSION_COOKIE } from "@/lib/auth";
import { db } from "@/lib/db";
import { parseEye } from "@/lib/overview";
import { printToPdf } from "@/lib/pdf";

export const runtime = "nodejs";

/** Phase 6: the overview page printed by Chromium (same page, print stylesheet, A4 landscape). */
export async function GET(req: NextRequest, { params }: { params: Promise<{ id: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id } = await params;
  const eye = parseEye(req.nextUrl.searchParams.get("eye") ?? undefined);
  const patient = await db.patient.findUnique({ where: { id }, select: { id: true } });
  if (!patient) return new NextResponse("não encontrado", { status: 404 });
  const cookie = req.cookies.get(SESSION_COOKIE);
  // The print page audits the access ("patient.overview.print") with the same session.
  const pdf = await printToPdf(new URL(`/print/patients/${id}/overview?eye=${eye}`, req.nextUrl.origin).toString(), cookie ? { name: cookie.name, value: cookie.value } : null);
  const name = `visao_geral_${new Date().toISOString().slice(0, 10)}_${eye}.pdf`;
  return new NextResponse(new Uint8Array(pdf), {
    headers: { "content-type": "application/pdf", "content-disposition": `attachment; filename="${name}"`, "cache-control": "no-store" },
  });
}
