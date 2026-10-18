import { currentUser } from "@/lib/auth";
import { audit } from "@/lib/audit";
import { loadView, subscribe } from "@/lib/live";
import type { LiveView } from "@/lib/live-types";

export const dynamic = "force-dynamic";

// Server-Sent Events: the latest snapshot now, then one event per snapshot the headset posts.
export async function GET(req: Request, { params }: { params: Promise<{ sessionId: string }> }) {
  const user = await currentUser();
  if (!user) return new Response("não autenticado", { status: 401 });
  const { sessionId } = await params;
  const first = await loadView(sessionId);
  if (!first) return new Response("sessão não encontrada", { status: 404 });
  await audit(user.id, "live.stream", "liveSession", sessionId, first.snapshot.patientRef ?? undefined);

  const enc = new TextEncoder();
  let unsubscribe = () => {};
  let heartbeat: ReturnType<typeof setInterval> | undefined;
  const stream = new ReadableStream<Uint8Array>({
    start(controller) {
      const send = (v: LiveView) => { try { controller.enqueue(enc.encode(`data: ${JSON.stringify(v)}\n\n`)); } catch { /* closed */ } };
      send(first);
      unsubscribe = subscribe(sessionId, send);
      heartbeat = setInterval(() => { try { controller.enqueue(enc.encode(": ping\n\n")); } catch { /* closed */ } }, 15_000);
      req.signal.addEventListener("abort", () => { unsubscribe(); clearInterval(heartbeat); try { controller.close(); } catch { /* already closed */ } });
    },
    cancel() { unsubscribe(); clearInterval(heartbeat); },
  });
  return new Response(stream, { headers: { "content-type": "text/event-stream; charset=utf-8", "cache-control": "no-store, no-transform", connection: "keep-alive", "x-accel-buffering": "no" } });
}
