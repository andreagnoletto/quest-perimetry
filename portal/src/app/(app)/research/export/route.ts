import { NextResponse, type NextRequest } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { examsCsv, pointsCsvResearch, researchRows } from "@/lib/research";

export async function GET(req: NextRequest) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  if (user.role !== "admin") return new NextResponse("sem permissão", { status: 403 });
  const type = req.nextUrl.searchParams.get("type") === "points" ? "points" : "exams";
  const rows = await researchRows();
  await audit(user.id, `research.export.${type}`, "research", undefined, `${new Set(rows.map((r) => r.studyId)).size} pacientes, ${rows.length} exames`);
  const body = type === "points" ? pointsCsvResearch(rows) : examsCsv(rows);
  return new NextResponse(body, {
    headers: { "content-type": "text/csv; charset=utf-8", "content-disposition": `attachment; filename="research_${type}_${new Date().toISOString().slice(0, 10)}.csv"`, "cache-control": "no-store" },
  });
}
