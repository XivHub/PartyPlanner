#!/usr/bin/env bash
# Reproducible Release x64 build for PartyPlanner on Linux.
#
# Needs the net10 SDK and the Dalamud dev libs. Override either via env:
#   DOTNET=/path/to/dotnet  DALAMUD_HOME=/path/to/dalamud/dev  ./build.sh
#
# The plugin targets net10.0-windows, so cross-building on Linux requires
# EnableWindowsTargeting (restores the Windows ref packs from NuGet).
set -euo pipefail

cd "$(dirname "$0")"

DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
[ -x "$DOTNET" ] || DOTNET="$(command -v dotnet)"

# Find Dalamud dev libs if not provided.
if [ -z "${DALAMUD_HOME:-}" ]; then
    for cand in "$HOME/.xlcore/dalamud/Hooks/dev" "$HOME/.cache/dalamud-dev"; do
        if [ -f "$cand/Dalamud.dll" ]; then
            DALAMUD_HOME="$cand"
            break
        fi
    done
fi
: "${DALAMUD_HOME:?Set DALAMUD_HOME to a folder containing Dalamud.dll}"

echo "dotnet:       $DOTNET"
echo "DALAMUD_HOME: $DALAMUD_HOME"

DALAMUD_HOME="$DALAMUD_HOME" "$DOTNET" build PartyPlanner/PartyPlanner.csproj \
    -c Release -p:Platform=x64 -p:EnableWindowsTargeting=true "$@"

echo
echo "Artifact: PartyPlanner/bin/x64/Release/PartyPlanner/latest.zip"
