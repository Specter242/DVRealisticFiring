#!/usr/bin/env bash
# Build the effects bundles, Realistic Firing's and Realistic Stack Effects'
# (the plume alone), with Unity 2019.4.40f1, the game's own engine version.
# Regenerates the smoke textures, then runs the editor headless to build the
# prefabs and pack them for Windows (the game's build).
# Needs an activated Unity license (sign in to Unity Hub once).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
project="$repo/fx/RealisticFiringFX"
out="$repo/fx/build"

[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 not found at $unity (set UNITY_EDITOR)" >&2; exit 1; }

mkdir -p "$out"
python3 -I "$repo/fx/textures/make_smoke_textures.py" "$project/Assets/RealisticFiring/Textures"
python3 -I "$repo/fx/textures/make_firebox_textures.py" "$project/Assets/RealisticFiring/Textures"
python3 -I "$repo/fx/textures/make_fire_volume.py" "$project/Assets/RealisticFiring/Textures"
# The rolling steam takes a few minutes: only when its script is newer.
roll="$project/Assets/RealisticFiring/Textures/smoke_roll_a.png"
if [[ ! -f "$roll" || "$repo/fx/textures/make_rolling_steam.py" -nt "$roll" ]]; then
    python3 -I "$repo/fx/textures/make_rolling_steam.py" "$project/Assets/RealisticFiring/Textures"
fi

if ! "$unity" -batchmode -nographics -quit -projectPath "$project" \
        -executeMethod RealisticFiringFX.PlumeBuilder.BuildFromCommandLine \
        -outputPath "$out" -logFile "$out/unity-build.log"; then
    grep -E "error|Error|Exception" "$out/unity-build.log" | tail -20 >&2 || true
    echo "Unity build failed; full log in $out/unity-build.log" >&2
    exit 1
fi
echo "Built $out/realisticfiring_fx and $out/realisticstackeffects_fx"

# The selected booklet is a separate bundle and does not enter Stack Effects,
# whose public source has no booklet to build.
if [[ -f "$repo/scripts/build-manual.sh" ]]; then
    "$repo/scripts/build-manual.sh"
fi
