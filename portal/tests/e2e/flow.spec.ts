import { execFileSync } from "node:child_process";
import { createHmac, randomBytes } from "node:crypto";
import { mkdtempSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, test } from "@playwright/test";

// Login → patient → exam order → headset exam (simulated by /core with the patient's code, signed with the
// device key) → import → Single Field Analysis → PDF → research export → access log.

const root = path.resolve(import.meta.dirname, "..", "..", "..");
const cli = ["dotnet", path.join(root, "artifacts", "bin", "Perimetry.Cli", "release", "perimetry.dll")];
const work = mkdtempSync(path.join(tmpdir(), "qp-e2e-"));
const deviceKey = randomBytes(32);

function simulate(code: string, eye: "OD" | "OS"): string {
  const out = path.join(work, `exam_20261018T100000_${eye}_${code}.json`);
  execFileSync(cli[0], [...cli.slice(1), "simulate", "--strategy", "ZestFast", "--defect", "SuperiorArcuate", "--eye", eye, "--seed", "4", "--age", "63", "--patient-ref", code, "--config", path.join(root, "config"), "--out", out]);
  writeFileSync(out + ".hmac", createHmac("sha256", deviceKey).update(readFileSync(out)).digest("hex"));
  return out;
}

test("doctor flow", async ({ page }) => {
  await page.goto("/");
  await expect(page).toHaveURL(/\/login$/);
  await expect(page.getByText("não registrado na ANVISA")).toBeVisible();
  await page.getByLabel("E-mail").fill("medica@teste.local");
  await page.getByLabel("Senha").fill("senha errada 123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByText("E-mail ou senha incorretos.")).toBeVisible();
  await page.getByLabel("Senha").fill("senha-de-teste-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Olá, Dra. Teste" })).toBeVisible();

  // patient
  await page.getByRole("link", { name: "Novo paciente" }).first().click();
  await page.getByLabel("Nome completo").fill("Maria Conceição da Silva");
  await page.getByLabel("ID interno (prontuário)").fill("PR-0001");
  await page.getByLabel("Data de nascimento").fill("1963-05-20");
  await page.getByLabel("Refração").fill("+2,00 esf");
  await page.getByLabel(/consentiu/).check();
  await page.getByLabel(/pesquisa/).check();
  await page.getByRole("button", { name: "Salvar" }).click();
  await expect(page.getByRole("heading", { name: "Maria Conceição da Silva" })).toBeVisible();
  const code = (await page.locator("b", { hasText: /^P-/ }).first().textContent())!.trim();
  expect(code).toMatch(/^P-[2-9A-Z]{6}$/);

  // search by name is exact and accent-insensitive
  await page.goto("/patients?q=maria conceicao da silva");
  await expect(page.getByRole("link", { name: "Maria Conceição da Silva" })).toBeVisible();

  // exam order for the headset
  await page.getByRole("link", { name: "Maria Conceição da Silva" }).click();
  await page.getByRole("button", { name: "Criar pedido de exame" }).click();
  await expect(page.getByText("Pedido criado.")).toBeVisible();
  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Baixar exam-order.json" }).first().click()]);
  const order = JSON.parse(readFileSync(await download.path(), "utf8"));
  expect(order).toMatchObject({ patientRef: code, presetId: "24-2-zest", refractiveCorrection: "+2,00 esf" });
  expect(JSON.stringify(order)).not.toContain("Maria");

  // headset with its integrity key
  await page.goto("/devices");
  const form = page.locator("section.card", { hasText: "Cadastrar headset" });
  await form.getByLabel(/Identificador/).fill("Q3-E2E-0001");
  await form.getByLabel("Nome").fill("Quest 3 teste");
  await form.getByLabel(/Chave de integridade/).fill(deviceKey.toString("base64"));
  await form.getByRole("button", { name: "Salvar headset" }).click();
  await expect(page.getByText("chave cadastrada")).toBeVisible();

  // calibration upload is validated by /core
  const cal = path.join(work, "calibration.json");
  writeFileSync(cal, JSON.stringify({ DeviceSerial: "Q3-E2E-0001", Photometer: "LS-160", MeasuredAtUtc: "2026-10-01T10:00:00Z", ValidUntilUtc: "2099-01-01T00:00:00Z", IsMeasured: true,
    Eyes: { OD: { Samples: [{ Level: 0, Cdm2: 0.01 }, { Level: 1, Cdm2: 98 }] }, OS: { Samples: [{ Level: 0, Cdm2: 0.01 }, { Level: 1, Cdm2: 99 }] } } }));
  const dev = page.locator("section.card", { hasText: "Q3-E2E-0001" });
  await dev.locator('input[type="file"]').setInputFiles(cal);
  await dev.getByRole("button", { name: "Enviar e validar" }).click();
  await expect(page.getByText("Calibração validada e ativada.")).toBeVisible();

  // import the exams written by the headset
  const od = simulate(code, "OD"), os = simulate(code, "OS");
  await page.goto("/exams/import");
  await page.locator('input[type="file"]').setInputFiles([od, od + ".hmac", os, os + ".hmac"]);
  await page.locator('select[name="deviceId"]').selectOption({ label: "Quest 3 teste (Q3-E2E-0001)" });
  await page.getByRole("button", { name: "Importar" }).click();
  await expect(page.getByText(`vinculado a ${code}`)).toHaveCount(2, { timeout: 60_000 });
  await expect(page.locator("tbody .badge.verified")).toHaveCount(2);

  // Single Field Analysis
  await page.getByRole("link", { name: path.basename(od) }).click();
  const sfa = page.locator("article.sfa");
  await expect(sfa.getByText("Análise de campo único")).toBeVisible();
  await expect(sfa.getByText("Olho direito (OD)")).toBeVisible();
  await expect(sfa.getByText("Maria Conceição da Silva")).toBeVisible();
  await expect(sfa.getByText(/PROVIS/).first()).toBeVisible();
  await expect(sfa.locator('[data-map$="-thr"] text')).toHaveCount(54);
  await expect(sfa.locator('[data-map$="-gray"] path[data-band]')).toHaveCount(54);
  await expect(sfa.getByText("GHT")).toBeVisible();
  await expect(sfa.getByText("integridade: verificada")).toBeVisible();

  if (process.env.E2E_OUT) await page.screenshot({ path: path.join(process.env.E2E_OUT, "report-screen.png"), fullPage: true });

  // patient overview: both eyes, grayscale series and index trends
  await page.getByRole("link", { name: /← Maria/ }).click();
  await expect(page.getByRole("heading", { name: "Olho direito (OD)" })).toBeVisible();
  await expect(page.getByRole("heading", { name: "Olho esquerdo (OS)" })).toBeVisible();
  await expect(page.getByText("realizado", { exact: false }).first()).toBeVisible(); // order fulfilled by the import
  if (process.env.E2E_OUT) await page.screenshot({ path: path.join(process.env.E2E_OUT, "patient-screen.png"), fullPage: true });

  // re-import is a duplicate
  await page.goto("/exams/import");
  await page.locator('input[type="file"]').setInputFiles([od]);
  await page.getByRole("button", { name: "Importar" }).click();
  await expect(page.getByText("exame já importado")).toBeVisible({ timeout: 60_000 });

  // PDF
  const examUrl = (await page.getByRole("link", { name: path.basename(od) }).getAttribute("href"))!;
  const pdf = await page.request.get(examUrl + "/pdf");
  expect(pdf.status()).toBe(200);
  expect(pdf.headers()["content-type"]).toBe("application/pdf");
  const bytes = await pdf.body();
  expect(bytes.subarray(0, 5).toString()).toBe("%PDF-");
  expect(bytes.length).toBeGreaterThan(20_000);
  writeFileSync(path.join(process.env.E2E_OUT ?? work, "report.pdf"), bytes);

  // pseudonymised research export
  const csv = await (await page.request.get("/research/export?type=exams")).text();
  expect(csv.split("\n")[0]).toContain("study_id");
  expect(csv.trim().split("\n").length).toBe(3);
  for (const leak of ["Maria", "PR-0001", code, "1963", "2026-10"]) expect(csv).not.toContain(leak);

  // access log
  await page.goto("/audit");
  for (const action of ["login", "patient.create", "patient.view", "order.download", "exam.import", "exam.view", "exam.print", "research.export.exams"]) {
    await expect(page.getByRole("cell", { name: action, exact: true }).first()).toBeVisible();
  }

  // unauthenticated access is refused
  await page.context().clearCookies();
  expect((await page.request.get(examUrl + "/export?format=raw")).status()).toBe(401);
  await page.goto(examUrl);
  await expect(page).toHaveURL(/\/login$/);
});
