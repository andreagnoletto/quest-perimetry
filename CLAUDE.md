# CLAUDE.md

Source of truth: **`SPEC.md`** (clinical product owner's specification, pt-BR). Read it before any work.
Implemented psychophysics: **`docs/CLINICAL_SPEC.md`**. Licences: **`docs/THIRD_PARTY.md`**.

## Working rules (from SPEC §0)
- Work in phases (SPEC §9). At the end of each phase: build + tests, short summary, **wait for the
  product owner's OK**.
- Anything with clinical impact: implement the HFA default, make it configurable, and **ask** before
  inventing something different. Never simplify the psychophysics silently.
- Every clinical constant lives in `config/*.json`, never in code. The config hash goes into every exam.
- Code and identifiers in English; patient/doctor UI in pt-BR (i18n ready for en-US, see
  `core/Perimetry.Core/Localization/Strings.cs`).
- Every exam logs every presentation (request + result + disposition) for audit and offline re-analysis.
- Never claim regulatory approval. Label: "Protótipo para pesquisa/uso sob supervisão médica — não registrado na ANVISA".
- Do not copy code from GPL projects (PyVF is GPL-3.0). OPI and visualFields are Apache-2.0: port with attribution.

## Layout
- `core/Perimetry.Core` — pure C# library (netstandard2.1, C# 9, only dependency Newtonsoft.Json) so the
  same sources compile in Unity 6. Namespaces: `Config`, `Geometry` (grids, Goldmann sizes, angular
  geometry), `Luminance` (dB scale, calibration), `Strategies` (ZEST, FT, fourTwo, screening, growth
  pattern), `Exam` (`ExamSession` = OPI-style server, catch trials, blind spot, records), `Analysis`
  (normative builder, TD/PD/MD/PSD/VFI/GHT, grayscale), `Simulation` (synthetic fields, observer, runner).
- `core/Perimetry.Core.Tests` — NUnit (net8.0); same tests can run in Unity Test Framework EditMode.
- `core/Perimetry.Cli` — `perimetry` CLI: simulate / analyze / benchmark / crosscheck / report (strict JSON for the
  portal) / check-calibration.
- `config/` — clinical constants and grids. `config/normative/` — provisional synthetic normative model.
- `tools/opi-crosscheck/` — compares C# ports with OPI in R. `tools/report/` — PNG report (matplotlib).
- `headset/` — Unity 6 project (phase 2). `Assets/Perimetry/Logic` is pure C# (`PatientFlow` state machine,
  `PresentationTimeline`, `StimulusPlacement`, `Dither`, `ExamStorage`, `PatientMessages`), tested by
  `headset/LogicTests`. `Runtime` (MonoBehaviours, head-locked rig), `Resources/Perimetry` (monocular dithering
  shaders), `Editor` (Configure Project, config sync), `Tests/EditMode`. `/core` is a local Unity package
  (`core/Perimetry.Core/package.json`); build output goes to `/artifacts` (Directory.Build.props) so no bin/
  lands inside the package. `headset/UnityCompileCheck` compiles the Unity scripts against UnityEngine reference DLLs.
- `portal/` — doctor portal (phase 3): Next.js 15 (App Router, server actions) + PostgreSQL via Prisma.
  `src/lib` (crypto/LGPD, auth, audit, importer, `perimetry.ts` = bridge to the CLI, pdf), `src/components`
  (`SingleFieldReport`, SVG `FieldMaps`), `src/app/(app)` pages, `src/app/print` (bare report the PDF prints).
  The portal never computes clinical numbers: every exam goes through `perimetry report` on import.

## Commands
```bash
dotnet build Perimetry.sln
dotnet test core/Perimetry.Core.Tests                                   # 122 tests, ~5 s
dotnet test headset/LogicTests                                           # 26 tests: headset flow, timing, geometry, dithering
dotnet build headset/UnityCompileCheck                                   # Unity scripts vs UnityEngine API (no Unity needed)
dotnet test core/Perimetry.Core.Tests --collect:"XPlat Code Coverage"   # coverage (cobertura xml)
dotnet run --project core/Perimetry.Cli -c Release -- simulate --strategy ZestFast --defect SuperiorArcuate --seed 4 --out out/ex.json
dotnet run --project core/Perimetry.Cli -c Release -- benchmark --strategy ZestFast --runs 30
python3 tools/report/render_report.py out/ex.analysis.json out/ex.png  # needs matplotlib
bash tools/opi-crosscheck/run.sh                                         # needs R + jsonlite, python3
cd portal && npm ci && npm run typecheck && npm test                     # portal unit tests (needs the Release CLI build)
cd portal && npm run build && npm run e2e                                # Playwright e2e; needs PostgreSQL (E2E_DATABASE_URL)
```
CLI options: `--grid 24-2|30-2|10-2|24-2C`, `--strategy Zest|ZestFast|FullThreshold|FourTwo|Screening`,
`--eye OD|OS`, `--defect Normal|SuperiorArcuate|InferiorArcuate|Hemianopia|CentralScotoma`, `--depth`,
`--age`, `--seed`, `--fpr`, `--fnr`, `--fixloss`, `--patient-ref`, `--fixation CentralHeijlKrakau|CentralEyeTracking|CentralUnmonitored`, `--config <dir>`.

## Conventions
- Field coordinates: degrees, patient's view, +x right, +y up; grids stored in OD convention, OS mirrors x.
- dB: **device dB** on the luminance increment, `10*log10(dLmax/dL)`; HFA-equivalent = device + `HfaOffsetDb`
  (≈15.5 dB nominal). Normative and grayscale use HFA-equivalent dB.
- Determinism: own PRNG (`MathUtil.Rng`, xoshiro256**), never `System.Random`.
- Ported OPI code must stay behaviour-identical: run `tools/opi-crosscheck/run.sh` after touching
  `ZestProcedure`, `FourTwoProcedure` or `FullThresholdProcedure`.
- Tests that change clinical defaults must be discussed with the product owner first.

## Clinical parameters (defaults; see config/ and docs/CLINICAL_SPEC.md)
| Item | Default |
|---|---|
| Background | 10 cd/m² (31.5 asb); nominal max 100 cd/m² until photometric calibration ("NÃO CALIBRADO") |
| Stimulus | Goldmann III (0.431°), 200 ms, white on white |
| Response window / ISI | 1000 ms / 600–1200 ms random; latency < 100 ms = anticipated (discarded) |
| ZEST | domain −5..35 dB, fpr=fnr=0.03, σ=1, bimodal prior 4:1, stop SD < 1.5 dB (fast: 2.0 dB) |
| Full Threshold | 4-2, last seen, re-test if > 4 dB from start |
| Screening | 6 dB brighter than age-expected, re-test not seen |
| Fixation | Mode A + Heijl-Krakau; blind spot (15°, −1.5°), remapped after 2 consecutive fixation losses |
| Catch trials | FP 1/30, HK 1/30, FN 1/40 (threshold − 9 dB) |
| Alerts | FL > 20%, FP > 15%, FN > 33% |
| Indices | MD inverse-variance (Heijl 1987), PSD (Heijl 1987), GH = 85th pct, VFI (Bengtsson 2008), GHT (Åsman & Heijl 1992) |
| Normative | PROVISIONAL synthetic, HFA-eq dB, −0.7 dB/decade |

## Headset conventions
- Stimuli are placed on a sphere centred on the TESTED eye (not the head); never in pixels.
- Clinical/presentation constants for the headset live in `config/headset.json`, `display.json`, `timing.json`.
- The flow never shows results to the patient. Unity layer only renders `FlowView` and feeds input edges + Tick.
- Unity, the Meta XR Simulator and APK builds cannot run in the cloud container: verify those on the product
  owner's machine (or a Remote Control session).

## Portal conventions
- Identifying patient fields are AES-256-GCM encrypted (`PERIMETRY_DATA_KEY`), searchable only by exact blind index.
  Only the pseudonymous code (`P-XXXXXX`) leaves the portal (exam-order.json, research export uses a separate study ID).
- Every page/route that reads patient data calls `requireUser` and writes an `audit()` entry.
- Report shows thresholds in HFA-equivalent dB, "<NN" below the device floor; PDF is the same page printed by Chromium.
- Use TrueType fonts and plain SVG geometry in the report: Chromium turns OTF fonts and SVG `<pattern>` fills into
  megabytes of Type 3 fonts / raster images in the PDF.
- Never run `prisma migrate reset` (or anything that drops data); e2e creates a new database per run.

## Open decisions (waiting for the product owner)
Tracked in `docs/CLINICAL_SPEC.md` as **[DECISÃO PENDENTE]**: GHT sector membership (`config/ght.json`,
status TO_BE_VALIDATED); headset pause, training, fixation target, 72 Hz duration (§11). Approved on 2026-10-17: ZEST stop SD 1.5 dB as default with ZestFast as an option,
grayscale edges, catch-trial frequencies, anticipated-response handling, no FN in screening. Approved on 2026-10-18: headset occlusion check before each eye (on).
