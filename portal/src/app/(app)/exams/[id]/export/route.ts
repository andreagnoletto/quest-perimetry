import { NextResponse, type NextRequest } from "next/server";
import { currentUser } from "@/lib/auth";
import { loadExam, pointsCsv } from "@/lib/exams";

export async function GET(req: NextRequest, { params }: { params: Promise<{ id: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id } = await params;
  const format = req.nextUrl.searchParams.get("format") ?? "json";
  if (!["json", "csv", "raw"].includes(format)) return new NextResponse("formato inválido", { status: 400 });
  const x = await loadExam(id, user.id, `exam.export.${format}`);
  if (!x) return new NextResponse("não encontrado", { status: 404 });
  const base = `exam_${x.exam.startedUtc.toISOString().slice(0, 10)}_${x.exam.eye}_${x.exam.examId}`;
  const headers = (type: string, ext: string) => ({ "content-type": type, "content-disposition": `attachment; filename="${base}${ext}"`, "cache-control": "no-store" });
  if (format === "raw") return new NextResponse(x.exam.rawText, { headers: headers("application/json; charset=utf-8", ".json") });
  if (format === "csv") return new NextResponse(pointsCsv(x.report), { headers: headers("text/csv; charset=utf-8", ".points.csv") });
  return new NextResponse(JSON.stringify(x.report, null, 2), { headers: headers("application/json; charset=utf-8", ".analysis.json") });
}
