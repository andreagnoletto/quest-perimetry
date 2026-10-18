import { NextResponse, type NextRequest } from "next/server";
import { currentUser, SESSION_COOKIE } from "@/lib/auth";
import { db } from "@/lib/db";
import { printToPdf } from "@/lib/pdf";

export const runtime = "nodejs";

export async function GET(req: NextRequest, { params }: { params: Promise<{ id: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id } = await params;
  const exam = await db.exam.findUnique({ where: { id }, select: { examId: true, eye: true, startedUtc: true } });
  if (!exam) return new NextResponse("não encontrado", { status: 404 });
  const cookie = req.cookies.get(SESSION_COOKIE);
  // The print page audits the access ("exam.print") with the same session.
  const pdf = await printToPdf(new URL(`/print/exams/${id}`, req.nextUrl.origin).toString(), cookie ? { name: cookie.name, value: cookie.value } : null);
  const name = `campo_${exam.startedUtc.toISOString().slice(0, 10)}_${exam.eye}.pdf`;
  return new NextResponse(new Uint8Array(pdf), {
    headers: { "content-type": "application/pdf", "content-disposition": `attachment; filename="${name}"`, "cache-control": "no-store" },
  });
}
