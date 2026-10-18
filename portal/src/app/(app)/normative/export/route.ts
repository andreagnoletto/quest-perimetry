import { NextResponse, type NextRequest } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { gridIds } from "@/lib/headset";
import { cohortForGrid } from "@/lib/normative";

/** The cohort file (perimetry-cohort/1) of a grid, pseudonymous, for offline analysis with `perimetry normative`. */
export async function GET(req: NextRequest) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  if (user.role === "operator") return new NextResponse("sem permissão", { status: 403 });
  const grid = req.nextUrl.searchParams.get("grid") ?? "24-2";
  if (!gridIds().includes(grid)) return new NextResponse("grade inválida", { status: 400 });
  const cohort = await cohortForGrid(grid);
  await audit(user.id, "normative.export", "normative", grid, `${cohort.volunteers} voluntários, ${cohort.file.subjects.length} exames`);
  return new NextResponse(cohort.json, {
    headers: { "content-type": "application/json; charset=utf-8", "content-disposition": `attachment; filename="cohort_${grid}_${new Date().toISOString().slice(0, 10)}.json"`, "cache-control": "no-store" },
  });
}
