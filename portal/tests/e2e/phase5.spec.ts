import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, test } from "@playwright/test";

// Phase 5: an exam order with the new options (10-2 grid, oculokinetic fixation, foveal threshold, forced choice),
// and the Single Field Analysis of every grid plus the foveal + gaze-trace report, rendered on the print page.
// With E2E_OUT set, each print page is saved as report-<name>.png for visual review.

const root = path.resolve(import.meta.dirname, "..", "..", "..");
const cli = ["dotnet", path.join(root, "artifacts", "bin", "Perimetry.Cli", "release", "perimetry.dll")];
const work = mkdtempSync(path.join(tmpdir(), "qp-e2e5-"));

function simulate(name: string, args: string[]): string {
  const out = path.join(work, `exam_20261018T110000_${name}.json`);
  execFileSync(cli[0], [...cli.slice(1), "simulate", "--strategy", "ZestFast", "--config", path.join(root, "config"), "--out", out, ...args]);
  return out;
}

test("phase 5 orders and reports", async ({ page }) => {
  await page.goto("/login");
  await page.getByLabel("E-mail").fill("medica@teste.local");
  await page.getByLabel("Senha").fill("senha-de-teste-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Olá, Dra. Teste" })).toBeVisible();

  await page.getByRole("link", { name: "Novo paciente" }).first().click();
  await page.getByLabel("Nome completo").fill("João Fase Cinco");
  await page.getByLabel("ID interno (prontuário)").fill("PR-0005");
  await page.getByLabel("Data de nascimento").fill("1970-01-15");
  await page.getByLabel(/consentiu/).check();
  await page.getByRole("button", { name: "Salvar" }).click();
  await expect(page.getByRole("heading", { name: "João Fase Cinco" })).toBeVisible();
  const code = (await page.locator("b", { hasText: /^P-/ }).first().textContent())!.trim();

  // order with every Phase 5 option
  await page.locator('select[name="gridId"]').selectOption("10-2");
  await page.locator('select[name="fixationMode"]').selectOption("Oculokinetic");
  await page.locator('select[name="responseMode"]').selectOption("ForcedChoice");
  await page.getByLabel("Limiar foveal").check();
  await expect(page.getByText(/Escolha forçada é experimental/)).toBeVisible();
  await page.getByRole("button", { name: "Criar pedido de exame" }).click();
  await expect(page.getByText("Pedido criado.")).toBeVisible();
  await expect(page.getByText("10-2 · Oculocinética (modo B) · limiar foveal · Escolha forçada (experimental)")).toBeVisible();
  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Baixar exam-order.json" }).first().click()]);
  const order = JSON.parse(readFileSync(await download.path(), "utf8"));
  expect(order).toEqual({ patientRef: code, ageYears: expect.any(Number), refractiveCorrection: null, presetId: "24-2-zest", grid: "10-2", fixationMode: "Oculokinetic", fovealThreshold: true, responseMode: "ForcedChoice" });

  // a default order (form reloaded, so the preset's own values) writes none of the new keys
  await page.reload();
  await page.getByRole("button", { name: "Criar pedido de exame" }).click();
  await expect(page.getByText("Pedido criado.")).toBeVisible();
  const [d2] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Baixar exam-order.json" }).first().click()]);
  expect(Object.keys(JSON.parse(readFileSync(await d2.path(), "utf8"))).sort()).toEqual(["ageYears", "patientRef", "presetId", "refractiveCorrection"]);

  // headset exams of every grid (simulated by /core), the 24-2 with foveal test + eye tracking
  const files: Record<string, string> = {
    "24-2-foveal-gaze": simulate("OD_24-2_fov", ["--grid", "24-2", "--defect", "SuperiorArcuate", "--seed", "4", "--age", "56", "--foveal", "true", "--gaze", "1.5", "--fixation", "CentralEyeTracking", "--patient-ref", code]),
    "10-2": simulate("OD_10-2", ["--grid", "10-2", "--defect", "CentralScotoma", "--seed", "5", "--age", "56", "--patient-ref", code]),
    "30-2": simulate("OS_30-2", ["--grid", "30-2", "--eye", "OS", "--defect", "InferiorArcuate", "--seed", "6", "--age", "56", "--patient-ref", code]),
    "24-2c": simulate("OD_24-2C", ["--grid", "24-2C", "--defect", "Normal", "--seed", "7", "--age", "56", "--patient-ref", code]),
    "forced-choice": simulate("OD_24-2_fc", ["--grid", "24-2", "--defect", "Normal", "--seed", "8", "--age", "56", "--fixation", "Oculokinetic", "--response", "ForcedChoice", "--patient-ref", code]),
  };
  await page.goto("/exams/import");
  await page.locator('input[type="file"]').setInputFiles(Object.values(files));
  await page.getByRole("button", { name: "Importar" }).click();
  await expect(page.getByText(`vinculado a ${code}`)).toHaveCount(5, { timeout: 120_000 });

  const hrefs: Record<string, string> = {};
  for (const [name, file] of Object.entries(files)) hrefs[name] = (await page.getByRole("link", { name: path.basename(file) }).getAttribute("href"))!;

  const points: Record<string, number> = { "24-2-foveal-gaze": 54, "10-2": 68, "30-2": 76, "24-2c": 64, "forced-choice": 54 };
  for (const name of Object.keys(files)) {
    await page.goto(hrefs[name].replace(/^\/exams\//, "/print/exams/"));
    const sfa = page.locator("article.sfa");
    await expect(sfa.locator('[data-map$="-thr"] text')).toHaveCount(points[name]);
    await expect(sfa.locator('[data-map$="-gray"] path[data-band]')).toHaveCount(points[name]);
    if (name === "24-2-foveal-gaze") {
      await expect(sfa.locator("dd.foveal")).toHaveText(/^\d+ dB/);
      await expect(sfa.locator("figure.gaze")).toBeVisible();
      expect(await sfa.locator("figure.gaze line.rej").count()).toBeGreaterThan(0); // SVG lines have no box: count, not visibility
      expect(await sfa.locator("figure.gaze line.err").count()).toBeGreaterThan(100);
      await expect(sfa.getByText(/Rastreamento do olhar: erro médio/)).toBeVisible();
      await expect(sfa.getByText("Olhar descartado")).toBeVisible();
      await expect(sfa.getByText("Central + losango de 4 pontos na fóvea")).toBeVisible();
    } else {
      await expect(sfa.locator("dd.foveal")).toHaveText("não testado");
      await expect(sfa.locator("figure.gaze")).toHaveCount(0);
    }
    if (name === "10-2") await expect(sfa.locator("p.ght")).toHaveText(/Não se aplica/);
    else await expect(sfa.locator("p.ght")).not.toHaveText(/Não se aplica/);
    if (name === "forced-choice") await expect(sfa.getByText("Oculocinética (modo B) · Escolha forçada (experimental)")).toBeVisible();
    if (process.env.E2E_OUT) await page.screenshot({ path: path.join(process.env.E2E_OUT, `report-${name}.png`), fullPage: true });
  }

  // the patient page lists the foveal value and the eye-tracking flag
  await page.goto("/patients");
  await page.getByRole("link", { name: "João Fase Cinco" }).click();
  await expect(page.getByText(/fóvea \d+ dB/)).toBeVisible();
  await expect(page.getByText("· eye-tracking")).toBeVisible();
  await expect(page.getByRole("cell", { name: "Não se aplica" })).toBeVisible();
});
