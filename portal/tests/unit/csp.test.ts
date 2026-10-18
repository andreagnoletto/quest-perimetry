import { describe, expect, it } from "vitest";
import { contentSecurityPolicy, newNonce } from "@/lib/csp";

describe("content security policy", () => {
  it("nonces are random base64 of 16 bytes", () => {
    const a = newNonce(), b = newNonce();
    expect(a).not.toBe(b);
    expect(Buffer.from(a, "base64").length).toBe(16);
  });
  it("production policy: nonce + strict-dynamic, no eval, no framing, same-origin forms", () => {
    const p = contentSecurityPolicy("abc", { dev: false, secure: false });
    expect(p).toContain("script-src 'self' 'nonce-abc' 'strict-dynamic'");
    expect(p).not.toContain("unsafe-eval");
    expect(p).toContain("frame-ancestors 'none'");
    expect(p).toContain("form-action 'self'");
    expect(p).toContain("object-src 'none'");
    expect(p).not.toContain("upgrade-insecure-requests");
  });
  it("dev allows eval for Fast Refresh; HTTPS adds upgrade-insecure-requests", () => {
    expect(contentSecurityPolicy("n", { dev: true, secure: false })).toContain("'unsafe-eval'");
    expect(contentSecurityPolicy("n", { dev: false, secure: true })).toContain("upgrade-insecure-requests");
  });
});
