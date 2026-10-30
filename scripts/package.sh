#!/usr/bin/env bash
# Build a Unity Mod Manager package from the public source tree.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
out="$repo/src/RealisticFiring/bin/Release/net48"
mp="$repo/src/RealisticFiring.Multiplayer/bin/Release/net48"
bundle="$repo/fx/build/realisticfiring_fx"
manual="$repo/fx/build/realisticfiring_manual"
version="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["Version"])' "$repo/src/RealisticFiring/Info.json")"
zipfile="$repo/dist/RealisticFiring-$version.zip"

dotnet test "$repo/tests/RealisticFiring.Core.Tests" -c Release -nologo -v quiet
dotnet test "$repo/tests/RealisticStackEffects.Core.Tests" -c Release -nologo -v quiet
"$repo/scripts/check-adapter.sh"
project="$repo/src/RealisticFiring/RealisticFiring.csproj"
if [[ -f "$repo/src/RealisticFiring.Multiplayer/RealisticFiring.Multiplayer.csproj" ]]; then
    project="$repo/src/RealisticFiring.Multiplayer/RealisticFiring.Multiplayer.csproj"
fi
dotnet build "$project" \
    -c Release -p:DvInstallDir="$dv" -warnaserror -nologo
# The add-on that feeds Realistic Stack Effects, loaded only when it runs.
se="$repo/src/RealisticFiring.StackEffects/bin/Release/net48"
dotnet build "$repo/src/RealisticFiring.StackEffects/RealisticFiring.StackEffects.csproj" \
    -c Release -p:DvInstallDir="$dv" -warnaserror -nologo

if [[ -x "$unity" ]]; then
    "$repo/scripts/build-fx.sh"
elif [[ ! -f "$bundle" ]]; then
    echo "No effects bundle at $bundle and no Unity to build one" >&2
    exit 1
fi

[[ -f "$manual" ]] || { echo "No firing manual bundle at $manual" >&2; exit 1; }

mkdir -p "$repo/dist"
stage="$(mktemp -d "$repo/dist/package.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/RealisticFiring"
cp "$out/Info.json" "$out/RealisticFiring.dll" "$out/RealisticFiring.Core.dll" \
    "$bundle" "$manual" "$repo/LICENSE" "$stage/RealisticFiring/"
# The players' firing guide, one page that opens in any browser.
python3 -I "$repo/scripts/build-guide.py" "$stage/RealisticFiring/FIRING-GUIDE.html" >/dev/null
files=(RealisticFiring/Info.json RealisticFiring/RealisticFiring.dll
       RealisticFiring/RealisticFiring.Core.dll RealisticFiring/realisticfiring_fx RealisticFiring/realisticfiring_manual
       RealisticFiring/LICENSE RealisticFiring/FIRING-GUIDE.html)
# The overlay's typeface rides in the effects bundle; its license goes beside it.
fonts="$repo/fx/RealisticFiringFX/Assets/RealisticFiring/Fonts"
if compgen -G "$fonts/*.ttf" >/dev/null; then
    cp "$fonts/Fira-OFL.txt" "$stage/RealisticFiring/"
    files+=(RealisticFiring/Fira-OFL.txt)
fi
if [[ "$project" == */RealisticFiring.Multiplayer.csproj ]]; then
    cp "$mp/RealisticFiring.Multiplayer.dll" "$stage/RealisticFiring/"
    files+=(RealisticFiring/RealisticFiring.Multiplayer.dll)
fi
cp "$se/RealisticFiring.StackEffects.dll" "$stage/RealisticFiring/"
files+=(RealisticFiring/RealisticFiring.StackEffects.dll)

# Select the archive's contents explicitly; never include assemblies copied
# for adapter checks, local settings, logs, or development documents.
rm -f "$zipfile"
(cd "$stage" && zip -q -X "$zipfile" "${files[@]}")
echo "Built $zipfile"
(cd "$(dirname "$zipfile")" && sha256sum "$(basename "$zipfile")")
