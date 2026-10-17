#!/usr/bin/env bash
# Downloads the OPI reference sources used by crosscheck.R (CRAN mirror on GitHub, pinned version).
set -euo pipefail
VERSION="${OPI_VERSION:-3.1.1}"
DEST="${1:-$(dirname "$0")/opi-src}"
mkdir -p "$DEST"
for f in zest.r fourTwo.r full_threshold.r; do
  curl -fsSL "https://raw.githubusercontent.com/cran/OPI/${VERSION}/R/${f}" -o "$DEST/$f"
done
echo "OPI ${VERSION} sources in $DEST"
