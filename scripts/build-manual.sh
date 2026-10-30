#!/usr/bin/env bash
# Build the already staged booklet pages. Run heavy Unity work through the offload queue.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 is required to build the manual" >&2; exit 1; }
mkdir -p "$repo/fx/build/manual"
"$unity" -batchmode -nographics -quit -projectPath "$repo/fx/RealisticFiringFX" \
    -executeMethod RealisticFiringFX.FiringManualBuilder.BuildFromCommandLine \
    -outputPath "$repo/fx/build/manual" -logFile "$repo/fx/build/manual-build.log"
cp "$repo/fx/build/manual/realisticfiring_manual" "$repo/fx/build/realisticfiring_manual"
rg 'FIRING MANUAL BUILD PASS:' "$repo/fx/build/manual-build.log"
