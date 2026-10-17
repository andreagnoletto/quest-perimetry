#!/usr/bin/env bash
# Full cross-check: scenarios -> C# results -> OPI in R -> comparison. Exit code 0 = identical.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
ROOT="$(cd "$HERE/../.." && pwd)"
WORK="${WORK:-$HERE/out}"
mkdir -p "$WORK"
[ -f "$WORK/opi-src/zest.r" ] || bash "$HERE/fetch_opi.sh" "$WORK/opi-src"
python3 "$HERE/make_scenarios.py" "$WORK/scenarios.json"
dotnet run --project "$ROOT/core/Perimetry.Cli" -c Release -- crosscheck "$WORK/scenarios.json" "$WORK/csharp.json"
Rscript "$HERE/crosscheck.R" "$WORK/scenarios.json" "$WORK/csharp.json" "$WORK/opi-src"
