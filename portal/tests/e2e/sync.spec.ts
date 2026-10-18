import { execFileSync } from "node:child_process";
import { createHmac, randomBytes } from "node:crypto";
import { mkdtempSync, readFileSync } from "node:fs";
import { tmpdir } from "node:os";
import path from "node:path";
import { expect, request, test } from "@playwright/test";

// Phase 4: pair a headset (API, no cookies) → order sent to it → live snapshots and the operator panel →
// pause command round trip → the headset uploads its files → exam imported, verified and the order fulfilled.
// Runs after flow.spec.ts in the same database (the doctor user exists).

const root = path.resolve(import.meta.dirname, "..", "..", "..");
const cli = ["dotnet", path.join(root, "artifacts", "bin", "Perimetry.Cli", "release", "perimetry.dll")];
const work = mkdtempSync(path.join(tmpdir(), "qp-e2e-sync-"));
const deviceKey = randomBytes(32);
const hmac = (text: string) => createHmac("sha256", deviceKey).update(Buffer.from(text, "utf8")).digest("hex");

function simulate(code: string, eye: "OD" | "OS"): { name: string; text: string; examId: string } {
  const out = path.join(work, `exam_20261018T150000_${eye}_${code}.json`);
  execFileSync(cli[0], [...cli.slice(1), "simulate", "--strategy", "ZestFast", "--defect", "Normal", "--eye", eye, "--seed", "7", "--age", "63", "--patient-ref", code, "--config", path.join(root, "config"), "--out", out]);
  const text = readFileSync(out, "utf8");
  return { name: path.basename(out), text, examId: (JSON.parse(text) as { ExamId: string }).ExamId };
}

const snapshot = (sessionId: string, seq: number, extra: Record<string, unknown>) => ({
  schema: "perimetry-sync/1", sessionId, seq, sentUtc: new Date().toISOString(), state: "Welcome", paused: false, finished: false, outcome: null,
  patientRef: null, orderId: null, presetId: null, ageYears: null, eye: null, startedUtc: new Date().toISOString(), elapsedMs: seq * 1000, exam: null, ...extra,
});

test("headset sync and live panel", async ({ page, baseURL }) => {
  await page.goto("/login");
  await page.getByLabel("E-mail").fill("medica@teste.local");
  await page.getByLabel("Senha").fill("senha-de-teste-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page.getByRole("heading", { name: "Olá, Dra. Teste" })).toBeVisible();

  // pairing code + portal.json
  await page.goto("/devices");
  await page.getByRole("button", { name: "Parear headset" }).click();
  const codeText = (await page.getByTestId("pairing-code").locator("b").textContent())!.trim();
  expect(codeText).toMatch(/^[2-9A-Z]{4}-[2-9A-Z]{4}$/);
  const [dl] = await Promise.all([page.waitForEvent("download"), page.getByRole("link", { name: "Baixar portal.json" }).click()]);
  const portalJson = JSON.parse(readFileSync(await dl.path(), "utf8"));
  expect(portalJson).toEqual({ url: baseURL, pairingCode: codeText });

  // the headset pairs (no cookies), a wrong code is refused
  const headset = await request.newContext({ baseURL });
  const bad = await headset.post("/api/headset/pair", { data: { code: "2222-3333", serial: "Q3-SYNC-0001", model: "Quest 3" } });
  expect(bad.status()).toBe(403);
  const pair = await headset.post("/api/headset/pair", { data: { code: codeText.toLowerCase(), serial: "Q3-SYNC-0001", model: "Quest 3", hmacKey: deviceKey.toString("base64"), software: "e2e" } });
  expect(pair.status()).toBe(200);
  const { token, deviceId } = await pair.json();
  expect(token).toMatch(/^[A-Za-z0-9_-]{40,}$/);
  const auth = { Authorization: `Bearer ${token}` };
  expect((await headset.post("/api/headset/pair", { data: { code: codeText, serial: "Q3-SYNC-0001" } })).status()).toBe(403); // single use
  expect((await headset.get("/api/headset/orders")).status()).toBe(401);
  await page.reload();
  await expect(page.locator("section.card", { hasText: "Q3-SYNC-0001" }).getByText("pareado")).toBeVisible();

  // patient + order sent to the (only) paired headset
  await page.goto("/patients/new");
  await page.getByLabel("Nome completo").fill("João Pereira Sync");
  await page.getByLabel("ID interno (prontuário)").fill("PR-SYNC-1");
  await page.getByLabel("Data de nascimento").fill("1960-02-10");
  await page.getByLabel(/consentiu/).check();
  await page.getByRole("button", { name: "Salvar" }).click();
  const code = (await page.locator("b", { hasText: /^P-/ }).first().textContent())!.trim();
  await expect(page.locator('select[name="deviceId"]')).toHaveValue(deviceId);
  await page.getByRole("button", { name: "Criar pedido de exame" }).click();
  await expect(page.getByText(/enviado ao headset/).first()).toBeVisible();

  const orders = await (await headset.get("/api/headset/orders", { headers: auth })).json();
  expect(orders.orders).toHaveLength(1);
  expect(orders.orders[0]).toMatchObject({ patientRef: code, presetId: "24-2-zest", ageYears: 66.7 });
  expect(JSON.stringify(orders)).not.toContain("João");
  const orderId: string = orders.orders[0].id;

  // live: welcome, then the exam with a few locations done
  const sessionId = "e2e" + Date.now().toString(36);
  const od = simulate(code, "OD");
  const rec = JSON.parse(od.text) as { Locations: { Index: number; X: number; Y: number; IsBlindSpot: boolean; ThresholdDb: number }[]; HfaOffsetDb: number };
  const r1 = await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 1, { patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7 }) });
  expect(r1.status()).toBe(200);
  expect((await r1.json()).commands).toEqual([]);
  const locations = rec.Locations.map((l, i) => ({ i: l.Index, x: l.X, y: l.Y, blindSpot: l.IsBlindSpot, state: i < 10 ? "Finished" : i < 14 ? "Active" : "NotStarted", db: i < 10 ? l.ThresholdDb : null, hfaEqDb: i < 10 ? l.ThresholdDb + rec.HfaOffsetDb : null, censoredLow: false, presentations: i < 14 ? 2 : 0 }));
  const exam = { examId: od.examId, gridId: "24-2", strategy: "ZestFast", eye: "OD", hfaOffsetDb: rec.HfaOffsetDb, minMeasurableHfaDb: rec.HfaOffsetDb, locations,
    reliability: { fixationLosses: { errors: 2, trials: 5, alertLimit: 0.2 }, falsePositives: { errors: 0, trials: 3, alertLimit: 0.15 }, falseNegatives: { errors: 0, trials: 1, alertLimit: 0.33 }, totalPresentations: 40, blindSpotFound: true, blindSpotRemaps: 0, durationMs: 60000 },
    last: { x: 9, y: -3, db: 24, seen: true, kind: "Threshold", tMs: 59000 } };
  await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 2, { state: "Exam", eye: "OD", patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7, exam }) });

  await page.goto("/live");
  await expect(page.getByRole("link", { name: code })).toBeVisible();
  await page.getByRole("link", { name: code }).click();
  await expect(page.getByRole("heading", { name: /João Pereira Sync/ })).toBeVisible();
  await expect(page.locator(".live-state")).toHaveText("Exame em andamento");
  await expect(page.locator('[data-map="live-map"] text')).toHaveCount(10);
  await expect(page.getByTestId("live-last")).toBeVisible();
  await expect(page.locator(".live-rel dd.alert")).toHaveText("2/5 xx"); // FL 40% > 20%
  if (process.env.E2E_OUT) await page.screenshot({ path: path.join(process.env.E2E_OUT, "live-panel.png"), fullPage: true });

  // SSE: a new snapshot reaches the open panel without reload
  await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 3, { state: "Exam", eye: "OD", patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7, exam: { ...exam, locations: locations.map((l, i) => (i < 20 ? { ...l, state: "Finished", db: 25, hfaEqDb: 25 + rec.HfaOffsetDb } : l)) } }) });
  await expect(page.locator('[data-map="live-map"] text')).toHaveCount(20);

  // pause from the portal → delivered on the next post → acked
  await page.getByRole("button", { name: "Pausar" }).click();
  await expect(page.getByText(/Pausa enviada ao headset/)).toBeVisible();
  const r4 = await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 4, { state: "Exam", eye: "OD", patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7, exam }) });
  const cmds = (await r4.json()).commands;
  expect(cmds).toHaveLength(1);
  expect(cmds[0]).toMatchObject({ type: "pause", by: "Dra. Teste" });
  const r5 = await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 5, { state: "Paused", paused: true, eye: "OD", patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7, exam, ackCommandIds: [cmds[0].id] }) });
  expect((await r5.json()).commands).toEqual([]);
  await expect(page.locator(".live-state")).toHaveText("Pausado (pausado)");
  await expect(page.getByRole("button", { name: "Retomar" })).toBeEnabled();
  await headset.post("/api/headset/live", { headers: auth, data: snapshot(sessionId, 6, { state: "Done", finished: true, outcome: "completed", eye: "OD", patientRef: code, orderId, presetId: "24-2-zest", ageYears: 66.7 }) });
  await expect(page.locator(".live-state")).toHaveText("Concluído");

  // upload: exam + session log (with the order id) → imported, verified by the key sent at pairing, order fulfilled
  const session = { SchemaVersion: "1.0", SessionId: sessionId, StartedUtc: new Date().toISOString(), DeviceSerial: "Q3-SYNC-0001", DeviceModel: "Quest 3", Events: [{ TMs: 0, Type: "state", Detail: "Welcome" }], ExamIds: [od.examId], Outcome: "completed", Order: { Id: orderId, PatientRef: code, PresetId: "24-2-zest" } };
  const sessionText = JSON.stringify(session, null, 2);
  const up = await headset.post("/api/headset/exams", { headers: auth, data: { sessionId, files: [
    { name: od.name, text: od.text, hmac: hmac(od.text) },
    { name: `session_20261018T150000_${sessionId}.json`, text: sessionText, hmac: hmac(sessionText) },
  ] } });
  expect(up.status()).toBe(200);
  const results = (await up.json()).results as { name: string; status: string; integrity: string; examDbId: string | null }[];
  expect(results.find((r) => r.name === od.name)).toMatchObject({ status: "imported", integrity: "verified" });
  expect(results.find((r) => r.name.startsWith("session_"))).toMatchObject({ status: "imported", integrity: "verified" });
  const again = await (await headset.post("/api/headset/exams", { headers: auth, data: { sessionId, files: [{ name: od.name, text: od.text, hmac: hmac(od.text) }] } })).json();
  expect(again.results[0].status).toBe("duplicate");

  const examDbId = results.find((r) => r.name === od.name)!.examDbId!;
  await page.goto(`/exams/${examDbId}`);
  await expect(page.locator("article.sfa").getByText("João Pereira Sync")).toBeVisible();
  await expect(page.locator("article.sfa").getByText("integridade: verificada")).toBeVisible();
  await page.getByRole("link", { name: /← João/ }).click();
  await expect(page.getByText(/realizado \(importado/)).toBeVisible();

  // revoke the token → headset locked out
  await page.goto("/devices");
  await page.locator("section.card", { hasText: "Q3-SYNC-0001" }).getByRole("button", { name: "revogar" }).click();
  await expect(page.locator("section.card", { hasText: "Q3-SYNC-0001" }).getByText("não pareado")).toBeVisible();
  expect((await headset.get("/api/headset/orders", { headers: auth })).status()).toBe(401);

  // access log names the headset
  await page.goto("/audit");
  for (const action of ["headset.pair", "headset.orders", "headset.live.start", "live.command", "headset.exam.upload", "device.token.revoke"]) {
    await expect(page.getByRole("cell", { name: action, exact: true }).first()).toBeVisible();
  }
  await headset.dispose();
});
