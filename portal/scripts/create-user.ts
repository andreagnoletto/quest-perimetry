// Creates or updates a portal user. Usage:
//   npm run create-user -- --email ana@clinica --name "Dra. Ana" --role admin    (password read from PERIMETRY_NEW_PASSWORD or stdin)
import { PrismaClient, type Role } from "@prisma/client";
import { createInterface } from "node:readline/promises";
import { hashPassword } from "../src/lib/password.ts";

function arg(name: string): string | undefined {
  const i = process.argv.indexOf("--" + name);
  return i >= 0 ? process.argv[i + 1] : undefined;
}

const email = arg("email")?.trim().toLowerCase();
const name = arg("name")?.trim();
const role = (arg("role") ?? "doctor") as Role;
if (!email || !name || !["admin", "doctor", "operator"].includes(role)) {
  console.error('uso: npm run create-user -- --email <email> --name "<nome>" [--role admin|doctor|operator]');
  process.exit(2);
}

let password = process.env.PERIMETRY_NEW_PASSWORD;
if (!password) {
  const rl = createInterface({ input: process.stdin, output: process.stderr });
  password = await rl.question("Senha (mínimo 12 caracteres): ");
  rl.close();
}

const db = new PrismaClient();
try {
  const passwordHash = await hashPassword(password);
  const u = await db.user.upsert({ where: { email }, update: { name, role, passwordHash, active: true }, create: { email, name, role, passwordHash } });
  await db.accessLog.create({ data: { action: "user.upsert", entityType: "user", entityId: u.id, detail: `${role} via script` } });
  console.log(`ok: ${u.email} (${u.role})`);
} catch (e) {
  console.error(e instanceof Error ? e.message : e);
  process.exitCode = 1;
} finally {
  await db.$disconnect();
}
