#!/usr/bin/env bash
# Build a Unity Mod Manager package from the public source tree.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
unity="${UNITY_EDITOR:-$HOME/Unity/Hub/Editor/2019.4.40f1/Editor/Unity}"
out="$repo/src/RealisticFiring/bin/Release/net48"
mp="$repo/src/RealisticFiring.Multiplayer/bin/Release/net48"
bundle="$repo/fx/build/realisticfiring_fx"
version="$(python3 -c 'import json, sys; print(json.load(open(sys.argv[1]))["Version"])' "$repo/src/RealisticFiring/Info.json")"
zipfile="$repo/dist/RealisticFiring-$version.zip"

dotnet test "$repo/tests/RealisticFiring.Core.Tests" -c Release -nologo -v quiet
"$repo/scripts/check-adapter.sh"
dotnet build "$repo/src/RealisticFiring.Multiplayer/RealisticFiring.Multiplayer.csproj" \
    -c Release -p:DvInstallDir="$dv" -warnaserror -nologo

if [[ -x "$unity" ]]; then
    "$repo/scripts/build-fx.sh"
elif [[ ! -f "$bundle" ]]; then
    echo "No effects bundle at $bundle and no Unity to build one" >&2
    exit 1
fi

mkdir -p "$repo/dist"
stage="$(mktemp -d "$repo/dist/package.XXXXXX")"
trap 'rm -rf "$stage"' EXIT
mkdir "$stage/RealisticFiring"
cp "$out/Info.json" "$out/RealisticFiring.dll" "$out/RealisticFiring.Core.dll" \
    "$mp/RealisticFiring.Multiplayer.dll" "$bundle" "$repo/LICENSE" "$stage/RealisticFiring/"

# Select the archive's contents explicitly; never include assemblies copied
# for adapter checks, local settings, logs, or development documents.
rm -f "$zipfile"
(cd "$stage" && zip -q -X "$zipfile" \
    RealisticFiring/Info.json RealisticFiring/RealisticFiring.dll \
    RealisticFiring/RealisticFiring.Core.dll RealisticFiring/RealisticFiring.Multiplayer.dll \
    RealisticFiring/realisticfiring_fx RealisticFiring/LICENSE)
echo "Built $zipfile"
(cd "$(dirname "$zipfile")" && sha256sum "$(basename "$zipfile")")
