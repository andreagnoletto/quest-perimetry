import { db } from "./db.ts";
import { LIVE_ACTIVE_MS, type LiveCommandOut, type LiveSnapshot, type LiveView } from "./live-types.ts";

// In-process live store: the latest snapshot per session and the subscribers (SSE streams) waiting for the
// next one. `next start` is a single process, so this is enough for a clinic LAN; the database keeps the
// last snapshot of every session (LiveSession) for restarts and history.

type Listener = (view: LiveView) => void;
interface Store { views: Map<string, LiveView>; listeners: Map<string, Set<Listener>>; listListeners: Set<() => void> }

const g = globalThis as unknown as { qpLive?: Store };
const store: Store = g.qpLive ?? { views: new Map(), listeners: new Map(), listListeners: new Set() };
g.qpLive = store;

export function publish(view: LiveView) {
  store.views.set(view.sessionId, view);
  for (const l of store.listeners.get(view.sessionId) ?? []) { try { l(view); } catch { /* listener gone */ } }
  for (const l of store.listListeners) { try { l(); } catch { /* ignore */ } }
  // forget finished sessions after a while so the map does not grow forever
  if (view.snapshot.finished) setTimeout(() => { if (store.views.get(view.sessionId) === view) store.views.delete(view.sessionId); }, 10 * 60_000).unref?.();
}

export function subscribe(sessionId: string, l: Listener): () => void {
  let set = store.listeners.get(sessionId);
  if (!set) { set = new Set(); store.listeners.set(sessionId, set); }
  set.add(l);
  return () => { set!.delete(l); if (set!.size === 0) store.listeners.delete(sessionId); };
}

export function subscribeList(l: () => void): () => void {
  store.listListeners.add(l);
  return () => { store.listListeners.delete(l); };
}

export function cached(sessionId: string): LiveView | undefined {
  return store.views.get(sessionId);
}

/** Latest view of a session: memory first, then the database row (after a restart). */
export async function loadView(sessionId: string): Promise<LiveView | null> {
  const mem = store.views.get(sessionId);
  if (mem) return mem;
  const row = await db.liveSession.findUnique({ where: { sessionId }, include: { device: true, commands: { where: { ackedAt: null }, include: {} } } });
  if (!row) return null;
  const view: LiveView = {
    sessionId,
    snapshot: row.snapshot as unknown as LiveSnapshot,
    receivedUtc: row.lastSeenUtc.toISOString(),
    deviceLabel: row.device?.label ?? row.device?.model ?? row.device?.serial ?? null,
    pendingCommands: await pendingCommands(row.id),
  };
  store.views.set(sessionId, view);
  return view;
}

export async function pendingCommands(liveSessionDbId: string): Promise<LiveCommandOut[]> {
  const cmds = await db.liveCommand.findMany({ where: { liveSessionId: liveSessionDbId, ackedAt: null }, orderBy: { issuedAt: "asc" } });
  if (!cmds.length) return [];
  const users = await db.user.findMany({ where: { id: { in: [...new Set(cmds.map((c) => c.issuedById))] } }, select: { id: true, name: true } });
  const name = new Map(users.map((u) => [u.id, u.name]));
  return cmds.map((c) => ({ id: c.id, type: c.type as LiveCommandOut["type"], issuedUtc: c.issuedAt.toISOString(), by: name.get(c.issuedById) ?? "—" }));
}

export const isActive = (lastSeen: Date | string, now = Date.now()) => now - new Date(lastSeen).getTime() < LIVE_ACTIVE_MS;

/** Sessions seen recently (active) plus the last finished ones, for the list page. */
export async function listSessions() {
  const rows = await db.liveSession.findMany({ orderBy: { lastSeenUtc: "desc" }, take: 30, include: { device: { select: { label: true, model: true, serial: true } } } });
  return rows.map((r) => ({
    sessionId: r.sessionId, patientRef: r.patientRef, state: r.state, finished: r.finished, outcome: r.outcome,
    startedUtc: r.startedUtc.toISOString(), lastSeenUtc: r.lastSeenUtc.toISOString(), active: !r.finished && isActive(r.lastSeenUtc),
    eye: (r.snapshot as unknown as LiveSnapshot).eye ?? null, deviceLabel: r.device?.label ?? r.device?.model ?? r.device?.serial ?? null,
  }));
}
