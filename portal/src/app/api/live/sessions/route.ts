import { NextResponse } from "next/server";
import { currentUser } from "@/lib/auth";
import { listSessions } from "@/lib/live";

export const dynamic = "force-dynamic";

/** Active and recent live sessions for the list page (polled by the browser). */
export async function GET() {
  const user = await currentUser();
  if (!user) return new NextResponse("não autenticado", { status: 401 });
  return NextResponse.json({ sessions: await listSessions() }, { headers: { "cache-control": "no-store" } });
}
