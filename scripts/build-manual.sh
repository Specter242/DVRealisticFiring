#!/usr/bin/env bash
# Build the already staged booklet pages. Run heavy Unity work through the offload queue.
# Records the bundle's inputs beside it (scripts/bundle-inputs.py), which
# packaging checks so a stale booklet cannot ship.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 is required to build the manual" >&2; exit 1; }
mkdir -p "$repo/fx/build/manual"
inputs="$(python3 -I "$repo/scripts/bundle-inputs.py" digest manual)"
"$unity" -batchmode -nographics -quit -projectPath "$repo/fx/RealisticFiringFX" \
    -executeMethod RealisticFiringFX.FiringManualBuilder.BuildFromCommandLine \
    -outputPath "$repo/fx/build/manual" -logFile "$repo/fx/build/manual-build.log"
cp "$repo/fx/build/manual/realisticfiring_manual" "$repo/fx/build/realisticfiring_manual"
rg 'FIRING MANUAL BUILD PASS:' "$repo/fx/build/manual-build.log"
python3 -I "$repo/scripts/bundle-inputs.py" write --built-from "$inputs" "$repo/fx/build/realisticfiring_manual"
