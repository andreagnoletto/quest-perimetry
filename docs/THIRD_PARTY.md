# Third-party code and data

Licences were checked in the packages' own `DESCRIPTION` / `LICENSE` files on 2026-10-17, before any
code was ported.

| Project | Version checked | Licence | How it is used here |
|---|---|---|---|
| **OPI — Open Perimetry Interface** (A. Turpin et al.), `github.com/turpinandrew/OPI`, CRAN mirror `github.com/cran/OPI` | 3.1.1 | **Apache License (>= 2)** (`DESCRIPTION`); file headers: "Modified Tue 21 Mar 2023: changed licence from gnu to Apache 2.0" | **Ported** to C#: `R/zest.r` → `core/Perimetry.Core/Strategies/ZestProcedure.cs`; `R/fourTwo.r` → `FourTwoProcedure.cs`; `R/full_threshold.r` → `FullThresholdProcedure.cs`; `R/dbTocd.r` formula → `Luminance/DbScale.cs`; `R/SimHenson.r`, `R/SimGaussian.r` response model → `Simulation/SimulatedObserver.cs`. Parameter names kept. Reference sources are downloaded (not vendored) by `tools/opi-crosscheck/fetch_opi.sh` for the R cross-check. |
| **visualFields** (I. Marín-Franch, W. H. Swanson), `github.com/imarinfr/vf1`, CRAN mirror `github.com/cran/visualFields` | 1.0.7 | **Apache License 2.0** (`DESCRIPTION`, `LICENSE`) | **Ported** formulas and data structure: `R/nv.R` (`nvgenerate`, `agelm`, `ghdef`, `pddef`, `lutdef`, `vfcomputevfi`) → `Analysis/NormativeDatabase.cs` (`NormativeBuilder`), `Analysis/GlobalIndices.cs`. **Data**: point coordinates of `locmaps` p24d2, p30d2, p10d2, p24d2c → `config/grids/*.json`. |
| **PyVF** (constructor-s), `github.com/constructor-s/PyVF` | master | **GNU GPL v3** (`LICENSE`) | **Not used. No code copied.** GPL-3.0 would require releasing this project under the GPL. Consulted only as a conceptual reference; the simulator here is derived from OPI. Do not copy from it. |
| **Newtonsoft.Json** (NuGet) | 13.0.3 | MIT | JSON serialisation in `/core` (same library Unity ships as `com.unity.nuget.newtonsoft-json`). |
| NUnit, NUnit3TestAdapter, Microsoft.NET.Test.Sdk, coverlet.collector (NuGet, tests only) | see csproj | MIT | Unit tests and coverage; not shipped. |
| matplotlib (Python, tools only) | any | matplotlib licence (PSF-style, BSD compatible) | `tools/report/render_report.py`; not shipped. |
| R 4.x + jsonlite (tools only) | — | GPL-2/3 (R), MIT (jsonlite) | Run `tools/opi-crosscheck/` as separate programs; no linking, nothing shipped. |

Algorithms implemented from the published literature (no code copied): MD/PSD (Heijl et al. 1987),
GHT (Åsman & Heijl 1992), VFI (Bengtsson & Heijl 2008), Heijl-Krakau fixation monitoring, ZEST prior
(Turpin et al. 2003), Hart/West normal CDF, Acklam normal quantile, xoshiro256** PRNG (public domain).

Apache-2.0 obligations (attribution and notice of modification) are met by `NOTICE`, by the header
comments in the ported files and by this table.

Commercial products named in SPEC §10 (Vivid Vision, Olleyes, Heru, Virtual Field, M&S, Oculera) are
functional references only; none of their assets or code is used.
