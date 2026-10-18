import type { NextConfig } from "next";

const config: NextConfig = {
  // Server-only modules that must not be bundled (native engine / browser automation).
  serverExternalPackages: ["@prisma/client", "playwright-core"],
  experimental: { serverActions: { bodySizeLimit: "20mb" } },
  poweredByHeader: false,
};

export default config;
