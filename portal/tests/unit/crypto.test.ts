import { randomBytes } from "node:crypto";
import { describe, expect, it } from "vitest";
import { blindIndex, decrypt, encrypt, normalise, randomCode } from "@/lib/crypto";

const key = randomBytes(32);

describe("crypto", () => {
  it("round-trips UTF-8 text and never repeats a ciphertext", () => {
    const a = encrypt("José da Silva Ção", key), b = encrypt("José da Silva Ção", key);
    expect(a).not.toBe(b);
    expect(a.startsWith("v1.")).toBe(true);
    expect(decrypt(a, key)).toBe("José da Silva Ção");
  });

  it("rejects tampered ciphertext and the wrong key", () => {
    const t = encrypt("12345", key);
    const parts = t.split(".");
    const ct = Buffer.from(parts[3], "base64url"); ct[0] ^= 1; parts[3] = ct.toString("base64url");
    expect(() => decrypt(parts.join("."), key)).toThrow();
    expect(() => decrypt(t, randomBytes(32))).toThrow();
  });

  it("blind index matches normalised text, depends on field and key", () => {
    expect(normalise("  MARIA  Conceição ")).toBe("maria conceicao");
    expect(blindIndex("Maria Conceição", "name", key)).toBe(blindIndex("maria conceicao", "name", key));
    expect(blindIndex("123", "name", key)).not.toBe(blindIndex("123", "internalId", key));
    expect(blindIndex("123", "name", key)).not.toBe(blindIndex("123", "name", randomBytes(32)));
  });

  it("pseudonymous codes use the unambiguous alphabet", () => {
    for (let i = 0; i < 200; i++) expect(randomCode()).toMatch(/^P-[2-9A-HJKMNP-Z]{6}$/);
  });
});
