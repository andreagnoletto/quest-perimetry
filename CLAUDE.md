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
- `core/Perimetry.Cli` — `perimetry` CLI: simulate / analyze / benchmark / crosscheck.
- `config/` — clinical constants and grids. `config/normative/` — provisional synthetic normative model.
- `tools/opi-crosscheck/` — compares C# ports with OPI in R. `tools/report/` — PNG report (matplotlib).
- `headset/` (Unity, phase 2), `portal/` (Next.js, phase 3) — not started.

## Commands
```bash
dotnet build Perimetry.sln
dotnet test core/Perimetry.Core.Tests                                   # 118 tests, ~5 s
dotnet test core/Perimetry.Core.Tests --collect:"XPlat Code Coverage"   # coverage (cobertura xml)
dotnet run --project core/Perimetry.Cli -c Release -- simulate --strategy ZestFast --defect SuperiorArcuate --seed 4 --out out/ex.json
dotnet run --project core/Perimetry.Cli -c Release -- benchmark --strategy ZestFast --runs 30
python3 tools/report/render_report.py out/ex.analysis.json out/ex.png  # needs matplotlib
bash tools/opi-crosscheck/run.sh                                         # needs R + jsonlite, python3
```
CLI options: `--grid 24-2|30-2|10-2|24-2C`, `--strategy Zest|ZestFast|FullThreshold|FourTwo|Screening`,
`--eye OD|OS`, `--defect Normal|SuperiorArcuate|InferiorArcuate|Hemianopia|CentralScotoma`, `--depth`,
`--age`, `--seed`, `--fpr`, `--fnr`, `--fixloss`, `--fixation CentralHeijlKrakau|CentralEyeTracking|CentralUnmonitored`, `--config <dir>`.

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
| Fixation | Mode A + Heijl-Krakau; blind spot (15°, −1.5°) |
| Catch trials | FP 1/30, HK 1/30, FN 1/40 (threshold − 9 dB) |
| Alerts | FL > 20%, FP > 15%, FN > 33% |
| Indices | MD inverse-variance (Heijl 1987), PSD (Heijl 1987), GH = 85th pct, VFI (Bengtsson 2008), GHT (Åsman & Heijl 1992) |
| Normative | PROVISIONAL synthetic, HFA-eq dB, −0.7 dB/decade |

## Open decisions (waiting for the product owner)
Tracked in `docs/CLINICAL_SPEC.md` as **[DECISÃO PENDENTE]**: default ZEST vs ZEST rápido; GHT sector
membership; grayscale band edges; catch-trial frequencies; anticipated-response handling; FN in screening.
