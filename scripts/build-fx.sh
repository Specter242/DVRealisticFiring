#!/usr/bin/env bash
# Build the chimney smoke effects bundle with Unity 2019.4.40f1, the game's
# own engine version. Regenerates the smoke textures, then runs the editor
# headless to build the prefab and pack it for Windows (the game's build).
# Needs an activated Unity license (sign in to Unity Hub once).
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
project="$repo/fx/RealisticFiringFX"
out="$repo/fx/build"

[[ -x "$unity" ]] || { echo "Unity 2019.4.40f1 not found at $unity (set UNITY_EDITOR)" >&2; exit 1; }

mkdir -p "$out"
python3 -I "$repo/fx/textures/make_smoke_textures.py" "$project/Assets/RealisticFiring/Textures"

if ! "$unity" -batchmode -nographics -quit -projectPath "$project" \
        -executeMethod RealisticFiringFX.PlumeBuilder.BuildFromCommandLine \
        -outputPath "$out" -logFile "$out/unity-build.log"; then
    grep -E "error|Error|Exception" "$out/unity-build.log" | tail -20 >&2 || true
    echo "Unity build failed; full log in $out/unity-build.log" >&2
    exit 1
fi
echo "Built $out/realisticfiring_fx"
