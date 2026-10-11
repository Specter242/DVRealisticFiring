#!/usr/bin/env bash
# Exercise production effects loading through Unity's native scene and asset
# cleanup. Uses an isolated headless editor, never the running game.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
bundle="${1:-$dv/Mods/RealisticFiring/realisticfiring_fx}"
log="$repo/fx/build/asset-check.log"

[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 not found at $unity" >&2; exit 1; }
[[ -f "$bundle" ]] || { echo "Effects bundle not found at $bundle" >&2; exit 1; }
[[ "$(basename "$bundle")" == realisticfiring_fx ]] || { echo "Expected a realisticfiring_fx bundle" >&2; exit 1; }
bundle="$(realpath "$bundle")"
project="$(mktemp -d /tmp/rf-asset-check-XXXXXX)"
trap 'rm -rf "$project"' EXIT
mkdir -p "$project/Assets" "$project/Packages" "$project/ProjectSettings" "$(dirname "$log")"
cp "$repo/fx/RealisticFiringFX/Packages/manifest.json" "$project/Packages/manifest.json"
cp "$repo/fx/RealisticFiringFX/ProjectSettings/ProjectVersion.txt" "$project/ProjectSettings/ProjectVersion.txt"
cp "$repo/src/RealisticFiring/Visuals/PlumeAssets.cs" "$project/Assets/PlumeAssets.cs"
cp "$repo/tools/RealisticFiring.AssetCheck/AssetLifetimeCheck.cs" "$project/Assets/AssetLifetimeCheck.cs"

if ! DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 timeout 180 "$unity" -batchmode -nographics -projectPath "$project" \
        -executeMethod AssetLifetimeCheck.Run -fxBundle "$bundle" -logFile "$log"; then
    tail -45 "$log" >&2
    exit 1
fi
rg 'ASSET CHECK PASS:|ALL ASSET LIFETIME CHECKS PASSED' "$log"
rg -q 'ALL ASSET LIFETIME CHECKS PASSED' "$log"
