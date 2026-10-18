import { defineConfig } from "@playwright/test";

// End-to-end test of the doctor flow against a production build and a throwaway database.
// Needs: PostgreSQL at E2E_DATABASE_URL, the /core CLI built in Release, `npm run build` done.
const PORT = Number(process.env.E2E_PORT ?? 3100);
// A new database per run (see global-setup): the main process fixes the name once; workers never touch the DB.
const db = new URL(process.env.E2E_DATABASE_URL ?? "postgresql://postgres@localhost:5433/perimetry_e2e");
if (!process.env.QP_E2E_DB) process.env.QP_E2E_DB = `${db.pathname.replace(/^\//, "")}_${Date.now()}`;
db.pathname = "/" + process.env.QP_E2E_DB;
const DATABASE_URL = db.toString();
const PERIMETRY_DATA_KEY = process.env.E2E_DATA_KEY ?? Buffer.alloc(32, 7).toString("base64");

process.env.DATABASE_URL = DATABASE_URL;
process.env.PERIMETRY_DATA_KEY = PERIMETRY_DATA_KEY;

export default defineConfig({
  testDir: "tests/e2e",
  timeout: 120_000,
  workers: 1,
  reporter: [["list"]],
  globalSetup: "./tests/e2e/global-setup.ts",
  use: { baseURL: `http://localhost:${PORT}`, locale: "pt-BR", timezoneId: "America/Sao_Paulo" },
  webServer: {
    command: `npx next start -p ${PORT}`,
    url: `http://localhost:${PORT}/login`, // not /api/health: the database is created by globalSetup, which may run after this
    reuseExistingServer: false,
    timeout: 60_000,
    env: { DATABASE_URL, PERIMETRY_DATA_KEY, NEXT_TELEMETRY_DISABLED: "1" },
  },
});
