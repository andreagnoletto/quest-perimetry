import { createHmac, timingSafeEqual } from "node:crypto";

/**
 * Verifies a headset file against its ".hmac" sidecar: lowercase hex HMAC-SHA256 of the exact file bytes
 * under the device key (headset/Assets/Perimetry/Logic/ExamStorage.cs). Integrity only, not a signature.
 */
export function verifyHmac(fileBytes: Buffer, sidecar: string, keyBase64: string): boolean {
  const key = Buffer.from(keyBase64.trim(), "base64");
  if (key.length < 32) return false;
  const want = createHmac("sha256", key).update(fileBytes).digest("hex");
  const got = sidecar.trim().toLowerCase();
  return got.length === want.length && timingSafeEqual(Buffer.from(got), Buffer.from(want));
}
