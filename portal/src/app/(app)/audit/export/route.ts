import { NextResponse, type NextRequest } from "next/server";
import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { db } from "@/lib/db";

const MAX_ROWS = 50_000;

const cell = (v: unknown) => {
  const s = v === null || v === undefined ? "" : v instanceof Date ? v.toISOString() : String(v);
  return /[",\n\r]/.test(s) ? `"${s.replace(/"/g, '""')}"` : s;
};

/** Access log as CSV (admin), with the same filters as the page; for LGPD evidence and offline review. */
export async function GET(req: NextRequest) {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  if (user.role !== "admin") return new NextResponse("sem permissão", { status: 403 });
  const sp = req.nextUrl.searchParams;
  const entity = sp.get("entity") || undefined, id = sp.get("id") || undefined;
  const rows = await db.accessLog.findMany({
    where: { ...(entity ? { entityType: entity } : {}), ...(id ? { entityId: id } : {}) },
    orderBy: { at: "desc" }, take: MAX_ROWS, include: { user: { select: { name: true, email: true } } },
  });
  await audit(user.id, "audit.export", "audit", undefined, `${rows.length} registros${entity ? `, tipo ${entity}` : ""}${id ? `, id ${id}` : ""}`);
  const head = ["at_utc", "user_name", "user_email", "action", "entity_type", "entity_id", "detail", "ip"];
  const lines = rows.map((r) => [r.at, r.user?.name, r.user?.email, r.action, r.entityType, r.entityId, r.detail, r.ip].map(cell).join(","));
  return new NextResponse([head.join(","), ...lines].join("\n") + "\n", {
    headers: { "content-type": "text/csv; charset=utf-8", "content-disposition": `attachment; filename="access_log_${new Date().toISOString().slice(0, 10)}.csv"`, "cache-control": "no-store" },
  });
}
