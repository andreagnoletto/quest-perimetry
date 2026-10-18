import { createCipheriv, createDecipheriv, createHmac, randomBytes, hkdfSync } from "node:crypto";
import { env } from "./env.ts";

// Application-level encryption of identifying fields (LGPD: data at rest). AES-256-GCM, random 96-bit IV,
// format "v1.<iv>.<tag>.<ciphertext>" (base64url). Separate sub-keys for encryption and blind indexes are
// derived from PERIMETRY_DATA_KEY with HKDF so one never doubles as the other.

function subKey(purpose: string, master: Buffer = env.dataKey): Buffer {
  return Buffer.from(hkdfSync("sha256", master, Buffer.alloc(0), "quest-perimetry/" + purpose, 32));
}

export function encrypt(plain: string, master?: Buffer): string {
  const iv = randomBytes(12);
  const c = createCipheriv("aes-256-gcm", subKey("enc", master), iv);
  const ct = Buffer.concat([c.update(plain, "utf8"), c.final()]);
  return ["v1", iv.toString("base64url"), c.getAuthTag().toString("base64url"), ct.toString("base64url")].join(".");
}

export function decrypt(token: string, master?: Buffer): string {
  const [v, iv, tag, ct] = token.split(".");
  if (v !== "v1" || !iv || !tag || ct === undefined) throw new Error("unsupported ciphertext");
  const d = createDecipheriv("aes-256-gcm", subKey("enc", master), Buffer.from(iv, "base64url"));
  d.setAuthTag(Buffer.from(tag, "base64url"));
  return Buffer.concat([d.update(Buffer.from(ct, "base64url")), d.final()]).toString("utf8");
}

export const encryptOpt = (v: string | null | undefined, master?: Buffer) => (v ? encrypt(v, master) : null);
export const decryptOpt = (v: string | null | undefined, master?: Buffer) => (v ? decrypt(v, master) : null);

/** Normalises text for exact matching: trimmed, lower case, accents removed, single spaces. */
export function normalise(s: string): string {
  return s.normalize("NFD").replace(/\p{M}/gu, "").trim().toLowerCase().replace(/\s+/g, " ");
}

/** Keyed blind index: equal inputs give equal outputs, without revealing the input. */
export function blindIndex(value: string, field: string, master?: Buffer): string {
  return createHmac("sha256", subKey("idx", master)).update(field + "\u0000" + normalise(value)).digest("hex");
}

const CODE_ALPHABET = "23456789ABCDEFGHJKMNPQRSTUVWXYZ"; // no 0/O, 1/I/L

/** Random pseudonymous code such as "P-7F3K2Q" (31^6 ≈ 8.9e8). */
export function randomCode(prefix = "P-", len = 6): string {
  const b = randomBytes(len);
  let s = "";
  for (let i = 0; i < len; i++) s += CODE_ALPHABET[b[i] % CODE_ALPHABET.length];
  return prefix + s;
}
