import { describe, expect, it } from "vitest";
import { hashPassword, verifyPassword } from "@/lib/password";

describe("password", () => {
  it("hashes with scrypt and verifies", async () => {
    const h = await hashPassword("correct horse battery");
    expect(h).toMatch(/^scrypt\$[\w-]+\$[\w-]+$/);
    expect(await verifyPassword("correct horse battery", h)).toBe(true);
    expect(await verifyPassword("correct horse batterY", h)).toBe(false);
    expect(await verifyPassword("x", "garbage")).toBe(false);
  });

  it("refuses short or predictable passwords (policy: 12+ characters)", async () => {
    await expect(hashPassword("short")).rejects.toThrow(/12 caracteres/);
    await expect(hashPassword("senha-curta")).rejects.toThrow(/12 caracteres/);
    await expect(hashPassword("aaaaaaaaaaaaaaaa")).rejects.toThrow(/repetir/);
    await expect(hashPassword("password12345")).rejects.toThrow(/previsível/);
    expect(await hashPassword("correct horse battery")).toMatch(/^scrypt\$/);
  });
});
