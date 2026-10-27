#!/usr/bin/env bash
# Exercise production effects loading through Unity's native scene and asset
# cleanup. Uses an isolated headless editor, never the running game. Checks
# Realistic Firing's loader against its bundle (the installed one unless
# given), then Realistic Stack Effects' against its bundle when there is one
# (the last built unless given).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
bundle="${1:-$dv/Mods/RealisticFiring/realisticfiring_fx}"
stack="${2:-$repo/fx/build/realisticstackeffects_fx}"

[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 not found at $unity" >&2; exit 1; }
[[ -f "$bundle" ]] || { echo "Effects bundle not found at $bundle" >&2; exit 1; }
[[ "$(basename "$bundle")" == realisticfiring_fx ]] || { echo "Expected a realisticfiring_fx bundle" >&2; exit 1; }

# check <loader source> <check source> <check class> <bundle> <log>
check() {
    local loader="$1" source="$2" method="$3" fx log="$5" project
    fx="$(realpath "$4")"
    project="$(mktemp -d /tmp/rf-asset-check-XXXXXX)"
    mkdir -p "$project/Assets" "$project/Packages" "$project/ProjectSettings" "$(dirname "$log")"
    cp "$repo/fx/RealisticFiringFX/Packages/manifest.json" "$project/Packages/manifest.json"
    cp "$repo/fx/RealisticFiringFX/ProjectSettings/ProjectVersion.txt" "$project/ProjectSettings/ProjectVersion.txt"
    cp "$loader" "$source" "$project/Assets/"
    if ! DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 timeout 180 "$unity" -batchmode -nographics -projectPath "$project" \
            -executeMethod "$method" -fxBundle "$fx" -logFile "$log"; then
        rm -rf "$project"
        tail -45 "$log" >&2
        exit 1
    fi
    rm -rf "$project"
    rg 'ASSET CHECK PASS:|ALL ASSET LIFETIME CHECKS PASSED' "$log"
    rg -q 'ALL ASSET LIFETIME CHECKS PASSED' "$log"
}

check "$repo/src/RealisticFiring/Visuals/PlumeAssets.cs" "$repo/tools/RealisticFiring.AssetCheck/AssetLifetimeCheck.cs" \
    AssetLifetimeCheck.Run "$bundle" "$repo/fx/build/asset-check.log"
if [[ -f "$stack" ]]; then
    [[ "$(basename "$stack")" == realisticstackeffects_fx ]] || { echo "Expected a realisticstackeffects_fx bundle" >&2; exit 1; }
    check "$repo/src/RealisticStackEffects/StackAssets.cs" "$repo/tools/RealisticStackEffects.AssetCheck/StackAssetsCheck.cs" \
        StackAssetsCheck.Run "$stack" "$repo/fx/build/stack-asset-check.log"
else
    echo "No Realistic Stack Effects bundle at $stack; its loader was not checked" >&2
fi
