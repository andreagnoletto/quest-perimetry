import path from "node:path";

/** Server configuration. Paths default to the monorepo layout (portal/ next to config/ and artifacts/). */
export const env = {
  get dataKey(): Buffer {
    const raw = process.env.PERIMETRY_DATA_KEY ?? "";
    const key = Buffer.from(raw, "base64");
    if (key.length !== 32) throw new Error("PERIMETRY_DATA_KEY must be 32 bytes, base64 (see .env.example)");
    return key;
  },
  get cli(): string[] {
    const c = process.env.PERIMETRY_CLI;
    if (c && c.trim()) return c.trim().split(/\s+/);
    return ["dotnet", path.resolve(process.cwd(), "..", "artifacts", "bin", "Perimetry.Cli", "release", "perimetry.dll")];
  },
  get configDir(): string {
    return path.resolve(process.cwd(), process.env.PERIMETRY_CONFIG_DIR ?? path.join("..", "config"));
  },
  get chromiumPath(): string | undefined {
    return process.env.PERIMETRY_CHROMIUM_PATH || undefined;
  },
  get secureCookies(): boolean {
    return process.env.PERIMETRY_SECURE_COOKIES === "true";
  },
  sessionTtlHours: 12,
};
