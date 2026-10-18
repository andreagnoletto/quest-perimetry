// In-process request limiter (Phase 6). Like the live store, it assumes `next start` = one process: counters live
// on globalThis and reset on restart. Used for the login form and headset pairing, where the credential itself is
// the secret and a slow brute force is the realistic attack. Per-account login lockout is stored in the database
// (User.failedLogins / lockedUntil) so it survives restarts; this limiter only adds a per-IP ceiling.

interface Bucket { hits: number[] }
const g = globalThis as unknown as { qpRate?: Map<string, Bucket> };
const store: Map<string, Bucket> = g.qpRate ?? new Map();
g.qpRate = store;

export interface RateDecision { allowed: boolean; remaining: number; retryAfterMs: number }

/** Sliding window: at most `limit` hits per `windowMs` for `key`. Records the hit when allowed. */
export function hit(key: string, limit: number, windowMs: number, now = Date.now()): RateDecision {
  const b = store.get(key) ?? { hits: [] };
  b.hits = b.hits.filter((t) => now - t < windowMs);
  if (b.hits.length >= limit) {
    store.set(key, b);
    return { allowed: false, remaining: 0, retryAfterMs: windowMs - (now - b.hits[0]) };
  }
  b.hits.push(now);
  store.set(key, b);
  if (store.size > 10_000) sweep(now, windowMs); // bound memory under a flood of distinct keys
  return { allowed: true, remaining: limit - b.hits.length, retryAfterMs: 0 };
}

export function reset(key: string) { store.delete(key); }

function sweep(now: number, windowMs: number) {
  for (const [k, b] of store) if (!b.hits.some((t) => now - t < windowMs)) store.delete(k);
}

// A clinic usually sits behind one NAT address, so the per-IP ceiling is generous; the per-account lockout
// (security-policy.ts) is the real brake on password guessing.
export const LOGIN_IP_LIMIT = { limit: 50, windowMs: 15 * 60_000 };
export const PAIRING_IP_LIMIT = { limit: 20, windowMs: 15 * 60_000 };

export const minutesLeft = (ms: number) => Math.max(1, Math.ceil(ms / 60_000));
