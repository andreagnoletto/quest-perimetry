import { NextResponse, type NextRequest } from "next/server";
import { contentSecurityPolicy, newNonce } from "@/lib/csp";

// Per-request CSP nonce (Phase 6). The static security headers live in next.config.ts; this one needs a nonce,
// so it is set here on both the request (for Next's inline scripts) and the response (for the browser).

export function middleware(req: NextRequest) {
  const nonce = newNonce();
  const csp = contentSecurityPolicy(nonce, {
    dev: process.env.NODE_ENV !== "production",
    secure: process.env.PERIMETRY_SECURE_COOKIES === "true",
  });
  const requestHeaders = new Headers(req.headers);
  requestHeaders.set("x-nonce", nonce);
  requestHeaders.set("content-security-policy", csp);
  const res = NextResponse.next({ request: { headers: requestHeaders } });
  res.headers.set("content-security-policy", csp);
  return res;
}

export const config = {
  matcher: [
    {
      source: "/((?!_next/static|_next/image|favicon.ico).*)",
      missing: [{ type: "header", key: "next-router-prefetch" }, { type: "header", key: "purpose", value: "prefetch" }],
    },
  ],
};
