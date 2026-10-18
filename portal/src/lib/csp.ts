// Content-Security-Policy (Phase 6). Built in middleware with a fresh nonce per request; Next.js reads the nonce
// from the request's CSP header and puts it on its own inline scripts, so no script runs without it. Uses only Web
// APIs (no node:crypto) because middleware runs in the edge runtime.

export function newNonce(): string {
  const bytes = new Uint8Array(16);
  crypto.getRandomValues(bytes);
  return btoa(String.fromCharCode(...bytes));
}

export interface CspOptions {
  /** Development server: React Fast Refresh needs eval. Never true in production. */
  dev: boolean;
  /** Behind HTTPS (PERIMETRY_SECURE_COOKIES=true): ask the browser to upgrade http:// sub-requests. */
  secure: boolean;
}

export function contentSecurityPolicy(nonce: string, o: CspOptions): string {
  const d = [
    "default-src 'self'",
    `script-src 'self' 'nonce-${nonce}' 'strict-dynamic'${o.dev ? " 'unsafe-eval'" : ""}`,
    // inline style attributes are used by React components (SVG maps, layout); no external stylesheet hosts
    "style-src 'self' 'unsafe-inline'",
    "img-src 'self' data: blob:",
    "font-src 'self'",
    "connect-src 'self'", // server actions, SSE live panel
    "frame-ancestors 'none'",
    "form-action 'self'",
    "base-uri 'self'",
    "object-src 'none'",
  ];
  if (o.secure) d.push("upgrade-insecure-requests");
  return d.join("; ");
}
