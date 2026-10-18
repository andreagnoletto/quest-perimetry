import { execFileSync } from "node:child_process";
import { expect, test, type Page } from "@playwright/test";

// Phase 6 security / LGPD: security headers and CSP, account lockout, password policy and change, patient data
// export, anonymisation, access-log CSV. Runs after flow.spec.ts in the same database (the admin user exists).

/** Submits the login form and waits for the server action to answer (the form keeps the previous message while a request is in flight). */
async function submitLogin(page: Page, email: string, password: string) {
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha").fill(password);
  await Promise.all([
    page.waitForResponse((r) => r.request().method() === "POST" && r.url().includes("/login")),
    page.getByRole("button", { name: "Entrar" }).click(),
  ]);
}

async function login(page: Page) {
  await page.goto("/login");
  await submitLogin(page, "medica@teste.local", "senha-de-teste-123");
  await expect(page.getByRole("heading", { name: "Olá, Dra. Teste" })).toBeVisible();
}

test("security headers and content security policy", async ({ page }) => {
  const res = await page.request.get("/login");
  const h = res.headers();
  expect(h["x-content-type-options"]).toBe("nosniff");
  expect(h["x-frame-options"]).toBe("DENY");
  expect(h["referrer-policy"]).toBe("strict-origin-when-cross-origin");
  expect(h["permissions-policy"]).toContain("camera=()");
  expect(h["content-security-policy"]).toMatch(/script-src 'self' 'nonce-[A-Za-z0-9+/=]+' 'strict-dynamic'/);
  expect(h["content-security-policy"]).toContain("frame-ancestors 'none'");
  expect(h["content-security-policy"]).not.toContain("unsafe-eval");
  expect(h["x-powered-by"]).toBeUndefined();

  // the page still works under the policy: no CSP violation while the client form hydrates and submits
  const violations: string[] = [];
  page.on("console", (m) => { if (/Content Security Policy/i.test(m.text())) violations.push(m.text()); });
  await page.goto("/login");
  await submitLogin(page, "medica@teste.local", "senha errada 123");
  await expect(page.getByText("E-mail ou senha incorretos.")).toBeVisible();
  expect(violations).toEqual([]);
});

test("account lockout after 10 failed logins", async ({ page }) => {
  execFileSync("node", ["scripts/create-user.ts", "--email", "bloqueio@teste.local", "--name", "Dr. Bloqueio", "--role", "operator"], {
    env: { ...process.env, PERIMETRY_NEW_PASSWORD: "senha-forte-do-bloqueio-1" }, stdio: "inherit",
  });
  await page.goto("/login");
  for (let i = 1; i <= 10; i++) {
    await submitLogin(page, "bloqueio@teste.local", `senha errada ${i}`);
    await expect(page.getByText(i < 10 ? "E-mail ou senha incorretos." : /Conta bloqueada temporariamente/)).toBeVisible();
  }
  // the right password is refused while locked
  await submitLogin(page, "bloqueio@teste.local", "senha-forte-do-bloqueio-1");
  await expect(page.getByText(/Conta bloqueada temporariamente/)).toBeVisible();
  // another account is unaffected
  await login(page);
  await page.goto("/audit?entity=user");
  await expect(page.getByRole("cell", { name: "login.locked", exact: true }).first()).toBeVisible();
});

test("password policy and change", async ({ page }) => {
  await login(page);
  await page.getByRole("link", { name: /Dra\. Teste/ }).click();
  await expect(page.getByRole("heading", { name: "Trocar senha" })).toBeVisible();
  const change = async (current: string, next: string) => {
    await page.getByLabel("Senha atual").fill(current);
    await page.getByLabel(/^Nova senha/).fill(next);
    await page.getByLabel("Confirmar nova senha").fill(next);
    await page.getByRole("button", { name: "Trocar senha" }).click();
  };
  await change("senha-de-teste-123", "aaaaaaaaaaaaaaaa");
  await expect(page.getByText(/não pode repetir/)).toBeVisible();
  await change("senha-de-teste-123", "password-12345678");
  await expect(page.getByText(/previsível/)).toBeVisible();
  await change("errada-errada-errada", "nova-senha-de-teste-456");
  await expect(page.getByText("A senha atual não confere.")).toBeVisible();
  await change("senha-de-teste-123", "nova-senha-de-teste-456");
  await expect(page.getByText(/Senha alterada/)).toBeVisible();
  // the session survived the rotation; put the password back for the next specs
  await change("nova-senha-de-teste-456", "senha-de-teste-123");
  await expect(page.getByText(/Senha alterada/)).toBeVisible();
});

test("patient data export and anonymisation (LGPD)", async ({ page }) => {
  await login(page);
  await page.goto("/patients/new");
  await expect(page.getByText(/LGPD, art\. 18/)).toBeVisible(); // consent text shown
  await page.getByLabel("Nome completo").fill("Carlos Anonimização Teste");
  await page.getByLabel("ID interno (prontuário)").fill("PR-ANON-1");
  await page.getByLabel("Data de nascimento").fill("1958-02-11");
  await page.getByLabel(/consentiu/).check();
  await page.getByLabel(/pesquisa/).check();
  await page.getByRole("button", { name: "Salvar" }).click();
  await expect(page.getByRole("heading", { name: "Carlos Anonimização Teste" })).toBeVisible();
  await expect(page.getByText(/texto v\d{4}-\d{2}-\d{2}\.\d+/)).toBeVisible(); // consent version recorded
  const url = page.url();
  const id = url.slice(url.lastIndexOf("/") + 1);
  const code = (await page.locator("b", { hasText: /^P-/ }).first().textContent())!.trim();

  // right of access: the export has the decrypted record
  const before = await page.request.get(`/patients/${id}/export`);
  expect(before.status()).toBe(200);
  expect(before.headers()["content-disposition"]).toContain(`paciente_${code}_`);
  const data = await before.json();
  expect(data.schema).toBe("perimetry-patient-export/1");
  expect(data.patient).toMatchObject({ name: "Carlos Anonimização Teste", internalId: "PR-ANON-1", birthDate: "1958-02-11", code });
  expect(data.consent.version).toBe(data.consent.currentVersion);

  // erasure: wrong confirmation is refused, the right one anonymises
  await page.getByRole("link", { name: "Anonimizar registro…" }).click();
  await expect(page.getByRole("heading", { name: /^Anonimizar Carlos/ })).toBeVisible();
  await page.getByLabel(/Motivo/).fill("pedido do titular");
  await page.getByLabel(/Para confirmar/).fill("P-XXXXXX");
  await page.getByRole("button", { name: "Anonimizar definitivamente" }).click();
  await expect(page.getByText(`Digite o código ${code} para confirmar.`)).toBeVisible();
  await page.getByLabel(/Motivo/).fill("pedido do titular"); // React resets the form after a failed action
  await page.getByLabel(/Para confirmar/).fill(code.toLowerCase());
  await page.getByRole("button", { name: "Anonimizar definitivamente" }).click();
  await expect(page.getByRole("heading", { name: "Paciente anonimizado" })).toBeVisible();
  await expect(page.getByText(/Registro anonimizado em/)).toBeVisible();
  await expect(page.getByRole("link", { name: "Editar" })).toHaveCount(0);
  await expect(page.getByRole("button", { name: "Criar pedido de exame" })).toHaveCount(0);

  // the name is gone everywhere: search, export, edit form
  await page.goto("/patients?q=carlos anonimizacao teste");
  await expect(page.getByText("Nenhum paciente encontrado.")).toBeVisible();
  await page.goto(`/patients?q=${code}`);
  await expect(page.getByRole("link", { name: "Paciente anonimizado" })).toBeVisible();
  await expect(page.getByText("anonimizado", { exact: true })).toBeVisible();
  const after = await (await page.request.get(`/patients/${id}/export`)).json();
  expect(after.patient.name).toBe("Paciente anonimizado");
  expect(JSON.stringify(after)).not.toContain("Carlos");
  expect(JSON.stringify(after)).not.toContain("PR-ANON-1");
  expect(JSON.stringify(after)).not.toContain("1958");
  await page.goto(`/patients/${id}/edit`);
  await expect(page).toHaveURL(new RegExp(`/patients/${id}$`));

  // access log: both actions, and the CSV export
  await page.goto(`/audit?entity=patient&id=${id}`);
  for (const action of ["patient.export", "patient.anonymize"]) await expect(page.getByRole("cell", { name: action, exact: true }).first()).toBeVisible();
  const csv = await page.request.get(`/audit/export?entity=patient&id=${id}`);
  expect(csv.status()).toBe(200);
  expect(csv.headers()["content-type"]).toContain("text/csv");
  const text = await csv.text();
  expect(text.split("\n")[0]).toBe("at_utc,user_name,user_email,action,entity_type,entity_id,detail,ip");
  expect(text).toContain("patient.anonymize");
  expect(text).toContain("pedido do titular");

  // unauthenticated access is refused
  await page.context().clearCookies();
  expect((await page.request.get(`/patients/${id}/export`)).status()).toBe(401);
  expect((await page.request.get("/audit/export")).status()).toBe(401);
});
