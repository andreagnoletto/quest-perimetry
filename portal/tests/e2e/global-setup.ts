import { execFileSync } from "node:child_process";

/**
 * Each run gets its own new database (name ends in a timestamp), migrated with the non-destructive
 * `prisma migrate deploy`, plus one admin user. Nothing existing is reset or dropped; old e2e databases
 * can be removed by hand.
 */
export default function globalSetup() {
  // DATABASE_URL was set to the new database by playwright.config.ts
  const env = { ...process.env, PERIMETRY_NEW_PASSWORD: "senha-de-teste-123" };
  execFileSync("npx", ["prisma", "migrate", "deploy"], { env, stdio: "inherit" });
  execFileSync("node", ["scripts/create-user.ts", "--email", "medica@teste.local", "--name", "Dra. Teste", "--role", "admin"], { env, stdio: "inherit" });
}
