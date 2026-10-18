import { db } from "./db.ts";
import { clientIp } from "./auth.ts";

/**
 * Access log (LGPD): who viewed, created, exported or changed what. Written for every patient-data access,
 * never for static pages. Entries are append-only from the application's point of view.
 */
export async function audit(userId: string | null, action: string, entityType?: string, entityId?: string, detail?: string) {
  await db.accessLog.create({
    data: { userId, action, entityType, entityId, detail, ip: await clientIp().catch(() => null) },
  });
}
