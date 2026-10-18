import type { NextConfig } from "next";

// Static security headers (Phase 6). The Content-Security-Policy needs a per-request nonce and is set in
// src/middleware.ts. HSTS only when the portal knows it is behind HTTPS (PERIMETRY_SECURE_COOKIES=true), because
// a wrong HSTS on a plain-HTTP clinic LAN would lock browsers out for a year.
const secure = process.env.PERIMETRY_SECURE_COOKIES === "true";
const securityHeaders = [
  { key: "X-Content-Type-Options", value: "nosniff" },
  { key: "X-Frame-Options", value: "DENY" },
  { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
  { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=(), payment=(), usb=()" },
  { key: "Cross-Origin-Opener-Policy", value: "same-origin" },
  ...(secure ? [{ key: "Strict-Transport-Security", value: "max-age=31536000; includeSubDomains" }] : []),
];

const config: NextConfig = {
  // Server-only modules that must not be bundled (native engine / browser automation).
  serverExternalPackages: ["@prisma/client", "playwright-core"],
  experimental: { serverActions: { bodySizeLimit: "20mb" } },
  poweredByHeader: false,
  async headers() {
    return [{ source: "/(.*)", headers: securityHeaders }];
  },
};

export default config;
