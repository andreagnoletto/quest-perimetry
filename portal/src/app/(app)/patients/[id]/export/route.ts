import { NextResponse } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";
import { patientExport } from "@/lib/lgpd";

/** Right of access (LGPD, art. 18 II): everything the portal holds about one patient, as one JSON file. */
export async function GET(_req: Request, { params }: { params: Promise<{ id: string }> }) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  const { id } = await params;
  const row = await db.patient.findUnique({ where: { id }, include: { exams: { orderBy: { startedUtc: "asc" } }, orders: { orderBy: { createdAt: "asc" } } } });
  if (!row) return new NextResponse("não encontrado", { status: 404 });
  const data = patientExport(row, row.orders, row.exams);
  await audit(user.id, "patient.export", "patient", id, `${row.exams.length} exames, ${row.orders.length} pedidos`);
  return new NextResponse(JSON.stringify(data, null, 2), {
    headers: {
      "content-type": "application/json; charset=utf-8",
      "content-disposition": `attachment; filename="paciente_${row.code}_${data.exportedUtc.slice(0, 10)}.json"`,
      "cache-control": "no-store",
    },
  });
}
