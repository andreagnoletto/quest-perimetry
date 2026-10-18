import { execFileSync } from "node:child_process";
import { mkdtempSync, readFileSync, statSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, test, type Page } from "@playwright/test";

// Phase 6: healthy volunteers → cohort page (/normative) → build → activate → re-analyse → report footer names the
// clinic normative; progression overview of one eye with its PDF. With E2E_OUT set, the cohort page and the overview
// are saved as PNG for visual review. Every exam is a /core simulation (no patient data).

const root = path.resolve(import.meta.dirname, "..", "..", "..");
const cli = ["dotnet", path.join(root, "artifacts", "bin", "Perimetry.Cli", "release", "perimetry.dll")];
const work = mkdtempSync(path.join(tmpdir(), "qp-e2e6-"));
const out = process.env.E2E_OUT;

function simulate(name: string, args: string[], startedUtc?: string): string {
  const file = path.join(work, `exam_${name}.json`);
  execFileSync(cli[0], [...cli.slice(1), "simulate", "--strategy", "ZestFast", "--config", path.join(root, "config"), "--out", file, ...args]);
  if (startedUtc) {
    // the simulator stamps "now": give the progression exams different dates (the file has no HMAC sidecar, so integrity stays "unverified")
    const doc = JSON.parse(readFileSync(file, "utf8").replace(/(:\s*)(-?Infinity|NaN)(?=\s*[,\]}])/g, "$1null")) as { StartedUtc: string };
    doc.StartedUtc = startedUtc;
    writeFileSync(file, JSON.stringify(doc));
  }
  return file;
}

async function login(page: Page) {
  await page.goto("/login");
  await page.getByLabel("E-mail").fill("medica@teste.local");
  await page.getByLabel("Senha").fill("senha-de-teste-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Olá, Dra. Teste" })).toBeVisible();
}

async function newPatient(page: Page, name: string, internalId: string, birthDate: string, volunteer: boolean): Promise<{ code: string; url: string }> {
  await page.goto("/patients/new");
  await page.getByLabel("Nome completo").fill(name);
  await page.getByLabel("ID interno (prontuário)").fill(internalId);
  await page.getByLabel("Data de nascimento").fill(birthDate);
  await page.getByLabel(/consentiu/).check();
  if (volunteer) await page.getByLabel(/Voluntário normal/).check();
  await page.getByRole("button", { name: "Salvar" }).click();
  await expect(page.getByRole("heading", { name })).toBeVisible();
  const code = (await page.locator("b", { hasText: /^P-/ }).first().textContent())!.trim();
  return { code, url: page.url() };
}

async function importFiles(page: Page, files: string[]) {
  await page.goto("/exams/import");
  await page.locator('input[type="file"]').setInputFiles(files);
  await page.getByRole("button", { name: "Importar" }).click();
  await expect(page.getByText(/vinculado a P-/)).toHaveCount(files.length, { timeout: 300_000 });
}

test.describe.configure({ timeout: 900_000 });

test("clinic normative and progression overview", async ({ page }) => {
  test.setTimeout(360_000); // simulates, imports and re-analyses several exams through the CLI
  await login(page);

  // 16 adult volunteers (ages 20–80) × 2 eyes = 32 eligible eyes (enough to build, not for MEASURED) + one minor
  // (excluded by age) whose exams show up in the exclusion table.
  const volunteers: { code: string; age: number }[] = [];
  for (let i = 0; i < 16; i++) {
    const age = 20 + i * 4;
    const { code } = await newPatient(page, `Voluntário Normal ${String(i + 1).padStart(2, "0")}`, `VN-${String(i + 1).padStart(4, "0")}`, `${2026 - age}-02-10`, true);
    volunteers.push({ code, age });
  }
  const minor = await newPatient(page, "Voluntário Menor", "VN-0099", "2012-05-05", true);
  await expect(page.getByText("voluntário normal")).toBeVisible();

  const reliable = ["--fpr", "0", "--fnr", "0", "--fixloss", "0", "--defect", "Normal"];
  const files: string[] = [];
  volunteers.forEach((v, i) => {
    files.push(simulate(`v${i}_OD`, [...reliable, "--seed", String(200 + i * 2), "--age", String(v.age), "--patient-ref", v.code]));
    files.push(simulate(`v${i}_OS`, [...reliable, "--eye", "OS", "--seed", String(201 + i * 2), "--age", String(v.age), "--patient-ref", v.code]));
  });
  files.push(simulate("minor_OD", [...reliable, "--seed", "300", "--age", "14", "--patient-ref", minor.code]));
  await importFiles(page, files);

  // the patient followed over time: three 24-2 of the right eye, one year apart, with a deepening defect
  const patient = await newPatient(page, "Paciente Progressão", "PR-0600", "1960-07-01", false);
  await expect(page.getByText("não é voluntário")).toBeVisible();
  const series = [
    simulate("p_2024", ["--defect", "SuperiorArcuate", "--depth", "6", "--seed", "401", "--age", "63", "--patient-ref", patient.code], "2024-03-01T13:00:00Z"),
    simulate("p_2025", ["--defect", "SuperiorArcuate", "--depth", "10", "--seed", "402", "--age", "64", "--patient-ref", patient.code], "2025-03-01T13:00:00Z"),
    simulate("p_2026", ["--defect", "SuperiorArcuate", "--depth", "14", "--seed", "403", "--age", "65", "--patient-ref", patient.code], "2026-03-01T13:00:00Z"),
  ];
  await importFiles(page, series);
  const examHref = (await page.getByRole("link", { name: path.basename(series[2]) }).getAttribute("href"))!;

  // ---- cohort page: /core's statistics of the volunteers' exams
  await page.goto("/normative?grid=24-2");
  await expect(page.getByRole("heading", { name: "Normativo próprio da clínica" })).toBeVisible();
  await expect(page.locator("[data-included]")).toHaveText("32");
  await expect(page.locator("[data-status]").first()).toHaveText("PROVISIONAL");
  await expect(page.getByText(/Normativo próprio continuará PROVISÓRIO até 100 olhos elegíveis \(há 32\)/)).toBeVisible();
  const excluded = page.locator('[data-table="excluded"] tbody tr');
  await expect(excluded).toHaveCount(1);
  await expect(excluded.first()).toContainText("idade fora da faixa");
  await expect(excluded.first().locator("a")).toHaveAttribute("href", /^\/exams\//);
  await expect(page.locator('[data-table="bands"] tbody tr')).toHaveCount(7);
  await expect(page.getByText("Nenhum ainda.")).toBeVisible();
  if (out) await page.screenshot({ path: path.join(out, "normative-cohort.png"), fullPage: true });

  // export: pseudonymous cohort file
  const [download] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Exportar coorte (JSON)" }).click()]);
  const cohort = JSON.parse(readFileSync(await download.path(), "utf8")) as { schema: string; gridId: string; subjects: { subjectId: string; thresholdsHfaDb: (number | null)[] }[] };
  expect(cohort.schema).toBe("perimetry-cohort/1");
  expect(cohort.gridId).toBe("24-2");
  expect(cohort.subjects).toHaveLength(33);
  expect(cohort.subjects.every((s) => /^S-[0-9a-f]{12}$/.test(s.subjectId) && s.thresholdsHfaDb.length === 54)).toBe(true);
  const text = JSON.stringify(cohort);
  expect(text).not.toContain("Voluntário");
  expect(text).not.toContain("P-");

  // build → stored, inactive
  await page.getByRole("button", { name: "Construir normativo" }).click();
  await expect(page.getByText(/construído \(PROVISIONAL, 32 olhos\)/)).toBeVisible({ timeout: 120_000 });
  const setRow = page.locator('[data-table="sets"] tbody tr');
  await expect(setRow).toHaveCount(1);
  await expect(setRow.first()).toContainText("inativo");
  const label = (await setRow.first().locator("b").textContent())!.trim();
  expect(label).toMatch(/^clinic-24-2-\d{4}-\d{2}-\d{2}$/);

  // activate, then re-analyse every 24-2 exam (36: 33 volunteers' + 3 of the patient)
  await setRow.first().getByRole("button", { name: "Ativar" }).click();
  await expect(page.getByText(`${label} é agora o normativo ativo da grade 24-2`)).toBeVisible();
  await expect(setRow.first()).toContainText("ativo desde");
  await page.getByRole("button", { name: "Reanalisar todos os exames desta grade" }).click();
  // Other specs may have imported 24-2 exams into the same database, so only "all of them" is asserted.
  const done = page.getByText(new RegExp(`(\\d+) de \\1 exames da grade 24-2 reanalisados com o normativo ${label}\\.`));
  await expect(done).toBeVisible({ timeout: 300_000 });
  const n = Number((await done.textContent())!.match(/^(\d+) de/)![1]);
  expect(n).toBeGreaterThanOrEqual(36);
  await expect(setRow.first().locator("td").nth(5)).toHaveText(String(n));

  // the report footer and footnotes name the clinic normative
  await page.goto(examHref.replace(/^\/exams\//, "/print/exams/"));
  const sfa = page.locator("article.sfa");
  // ages at the exam date: 20.7–80.7 → "21–81 anos"
  await expect(sfa.locator("footer")).toContainText(new RegExp(`normativo próprio ${label} \\(32 olhos, 2\\d–8\\d anos, provisório\\)`));
  await expect(sfa.locator("footer")).toContainText("NORMATIVO PRÓPRIO PROVISÓRIO — 32 olhos (mínimo 100)");
  if (out) await page.screenshot({ path: path.join(out, "report-clinic-normative.png"), fullPage: true });

  // a new import uses the active set straight away; the per-exam button re-analyses one exam
  const late = simulate("p_2026b", ["--defect", "SuperiorArcuate", "--depth", "14", "--seed", "404", "--age", "65", "--patient-ref", patient.code], "2026-09-01T13:00:00Z");
  await importFiles(page, [late]);
  const lateHref = (await page.getByRole("link", { name: path.basename(late) }).getAttribute("href"))!;
  await page.goto(lateHref);
  await expect(page.getByText(new RegExp(`normativo próprio ${label}`)).first()).toBeVisible();
  await page.getByRole("button", { name: "Reanalisar" }).click();
  await expect(page.getByText(new RegExp(`normativo próprio ${label}`)).first()).toBeVisible();

  // deactivate → new analyses go back to the synthetic normative; existing ones keep theirs until re-analysed
  await page.goto("/normative?grid=24-2");
  await page.getByRole("button", { name: "Desativar" }).click();
  await expect(page.getByText(/desativado: novas análises da grade 24-2 voltam ao normativo provisório sintético/)).toBeVisible();
  await page.goto(lateHref);
  await page.getByRole("button", { name: "Reanalisar" }).click();
  await expect(page.locator("article.sfa footer")).toContainText("normativo provisório sintético");
  await page.goto("/normative?grid=24-2");
  await setRow.first().getByRole("button", { name: "Ativar" }).click();
  await expect(setRow.first()).toContainText("ativo desde");

  // ---- progression overview of the right eye: 4 exams in date order, trend from the 4 MD values
  await page.goto(patient.url);
  await page.getByRole("link", { name: "Visão geral (progressão)" }).first().click();
  await expect(page).toHaveURL(/\/overview\?eye=OD$/);
  const ovw = page.locator("article.ovw");
  await expect(ovw.locator(".ovw-exam")).toHaveCount(4);
  await expect(ovw.locator(".ovw-exam .ovw-date b")).toHaveText(["01/03/2024", "01/03/2025", "01/03/2026", "01/09/2026"]);
  await expect(ovw.locator('[data-map$="-gray"]')).toHaveCount(4);
  await expect(ovw.locator('[data-map$="-tdp"]')).toHaveCount(4);
  await expect(ovw.locator("figure.trend")).toHaveCount(2);
  await expect(ovw.locator("figure.trend").first()).toContainText(/tendência -?\d+,\d+ dB\/ano ± \d+,\d+ \(p = \d,\d+\) · 4 exames em 2,5 anos/);
  await expect(ovw.locator("footer")).toContainText("não é análise de eventos (GPA)");
  await expect(ovw.locator("footer")).toContainText(`normativo próprio ${label}`);
  if (out) await page.screenshot({ path: path.join(out, "overview.png"), fullPage: true });

  // the other eye has nothing; the PDF is the same page printed in landscape
  await page.getByRole("link", { name: "Ver OS" }).click();
  await expect(page.locator("article.ovw")).toContainText("Nenhum exame de limiar concluído para este olho.");
  await page.goto(patient.url + "/overview?eye=OD");
  const [pdf] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "PDF (A4 paisagem)" }).click()]);
  const pdfPath = await pdf.path();
  expect(pdf.suggestedFilename()).toMatch(/^visao_geral_\d{4}-\d{2}-\d{2}_OD\.pdf$/);
  const size = statSync(pdfPath).size;
  expect(size).toBeGreaterThan(10_000);
  expect(size).toBeLessThan(3_000_000);
  expect(readFileSync(pdfPath).subarray(0, 5).toString()).toBe("%PDF-");

  // access log names every normative step
  const expectLogged = async (entity: string, actions: string[]) => {
    await page.goto(`/audit?entity=${entity}`);
    for (const a of actions) await expect(page.getByRole("cell", { name: a, exact: true }).first()).toBeVisible();
  };
  await expectLogged("normative", ["normative.view", "normative.export", "normative.build", "normative.activate", "normative.reanalyze", "normative.deactivate"]);
  await expectLogged("exam", ["exam.reanalyze"]);
  await expectLogged("patient", ["patient.overview", "patient.overview.print"]);
});
