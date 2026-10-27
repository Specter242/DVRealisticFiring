#!/usr/bin/env bash
# Run the sim adapter's compatibility checks against the installed game's
# assemblies under Mono. Unity's native side is not needed.
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dv="${DV_INSTALL_DIR:-$HOME/.local/share/Steam/steamapps/common/Derail Valley}"
project="$repo/tools/RealisticFiring.AdapterCheck"
stack="$repo/tools/RealisticStackEffects.AdapterCheck"

command -v mono >/dev/null || { echo "mono is required" >&2; exit 1; }
# Fail on a failed build or any compiler warning; never run a stale checker.
for csproj in "$project/RealisticFiring.AdapterCheck.csproj" "$stack/RealisticStackEffects.AdapterCheck.csproj"; do
    if ! build="$(dotnet build "$csproj" -c Release -p:DvInstallDir="$dv" -nologo 2>&1)"; then
        grep -E "error" <<<"$build" | sort -u >&2
        echo "adapter check build failed" >&2
        exit 1
    fi
    if grep -q -E "warning CS" <<<"$build"; then
        grep -E "warning CS" <<<"$build" | sort -u >&2
        exit 1
    fi
done
# Optional Multiplayer, Skin Manager, and DV Seasons checks load their
# installed assemblies and dependencies in place, never copying third-party
# mod binaries into a package; missing ones are skipped. Further assemblies
# (another copy of Skin Manager, say) can be given as arguments.
status=0
mono "$project/bin/Release/net48/RealisticFiring.AdapterCheck.exe" \
    "$dv/Mods/Multiplayer/Multiplayer.dll" "$dv/Mods/SkinManagerMod/SkinManagerMod.dll" \
    "$dv/Mods/DVSeasons/DVSeasons.dll" "$@" || status=$?
# Realistic Stack Effects' reading of the stock firebox and exhaust.
mono "$stack/bin/Release/net48/RealisticStackEffects.AdapterCheck.exe" || status=$?
exit "$status"
