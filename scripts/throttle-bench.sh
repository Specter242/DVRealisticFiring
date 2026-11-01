#!/usr/bin/env bash
# Offline patched-engine bench. Run a full matrix through the offload queue;
# --quick is one short fixture for checking the harness itself.
set -euo pipefail
repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
[[ $# -ge 1 && $# -le 3 ]] || { echo 'Usage: throttle-bench.sh EMPTY_OUTPUT_DIRECTORY [--sensitivity] [--quick]' >&2; exit 2; }
destination="$1"
shift
mode=--throttle-bench
quick=()
for option in "$@"; do
    case "$option" in
        --sensitivity) mode=--throttle-sensitivity ;;
        --quick) quick=(--quick) ;;
        *) echo "Unknown option: $option" >&2; exit 2 ;;
    esac
done
if [[ ${#quick[@]} == 0 && -z "${OFFLOAD_JOB:-}" ]]; then
    echo 'Run the full matrix through the offload queue; --quick is the short harness check.' >&2
    exit 2
fi
project="$repo/tools/RealisticFiring.AdapterCheck/RealisticFiring.AdapterCheck.csproj"
dotnet build "$project" -c Release -warnaserror -nologo > /dev/null
mono "$repo/tools/RealisticFiring.AdapterCheck/bin/Release/net48/RealisticFiring.AdapterCheck.exe" "$mode" "$destination" "${quick[@]}"
