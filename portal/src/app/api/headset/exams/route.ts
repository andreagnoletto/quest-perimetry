import { z } from "zod";
import { auditDevice, authenticateDevice, readJson, syncError, syncJson } from "@/lib/headset-auth";
import { importFiles } from "@/lib/importer";

export const dynamic = "force-dynamic";

// POST /api/headset/exams — the headset's exam_*.json / session_*.json files, raw text + HMAC sidecar.
const schema = z.object({
  sessionId: z.string().max(64).optional().nullable(),
  files: z.array(z.object({
    name: z.string().regex(/^(exam|session)_[A-Za-z0-9_.-]{1,120}\.json$/),
    text: z.string().min(2).max(16 * 1024 * 1024),
    hmac: z.string().regex(/^[0-9a-fA-F]{64}$/).optional().nullable(),
  })).min(1).max(10),
});

export async function POST(req: Request) {
  const device = await authenticateDevice(req);
  if (!device) return syncError("unauthorized", "token inválido ou revogado", 401);
  const parsed = schema.safeParse(await readJson(req));
  if (!parsed.success) return syncError("bad_request", "files[] com name, text e hmac (até 10 por chamada)", 400);

  const uploaded = parsed.data.files.flatMap((f) => {
    const bytes = Buffer.from(f.text, "utf8");
    return f.hmac ? [{ name: f.name, bytes }, { name: f.name + ".hmac", bytes: Buffer.from(f.hmac.toLowerCase(), "ascii") }] : [{ name: f.name, bytes }];
  });
  const outcomes = await importFiles(uploaded, { userId: null, deviceId: device.id });
  const results = outcomes.filter((o) => o.kind !== "hmac").map((o) => ({
    name: o.file, status: o.status === "skipped" ? "error" : o.status, message: o.message, examDbId: o.examDbId ?? null, integrity: o.integrity ?? null,
  }));
  for (const o of outcomes.filter((x) => x.kind === "exam" && x.status === "imported")) {
    await auditDevice(device, "headset.exam.upload", "exam", o.examDbId, `${o.file}; integrity=${o.integrity}; patient=${o.patientCode ?? "none"}`, req);
  }
  if (!outcomes.some((x) => x.kind === "exam" && x.status === "imported")) await auditDevice(device, "headset.exam.upload", "device", device.id, `${results.length} files, none new`, req);
  return syncJson({ results });
}

