#!/usr/bin/env bash
# Builds the patcher and its helper into out/.
#
#   GAME_DIR=DIR ./build.sh
#
# DIR holds the game's slipstream.dll and FNA.dll, which the helper compiles against and
# which never ship; tools/unbundle.py extracts slipstream.dll from the Linux game. DOTNET
# names a .NET 8 SDK's dotnet when it is not the one on PATH.
set -euo pipefail

here=$(cd "$(dirname "$0")" && pwd)
dotnet=${DOTNET:-dotnet}
game_dir=$(cd "${GAME_DIR:?GAME_DIR must name the folder with slipstream.dll and FNA.dll}" && pwd)
# The trimmed game references System.Private.CoreLib: the newest .NET 8 runtime of that SDK.
runtime_dir=$("$dotnet" --list-runtimes |
  awk '$1 == "Microsoft.NETCore.App" && $2 ~ /^8\./ {print $2, substr($3, 2, length($3) - 2) "/" $2}' |
  sort -V | tail -1 | cut -d' ' -f2-)
if [ -z "$runtime_dir" ]; then
  echo "no .NET 8 runtime beside $dotnet" >&2
  exit 1
fi
work="$here/artifacts"
out="$here/out"
export DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1

"$dotnet" build "$here/src/SlipstreamAspect/SlipstreamAspect.csproj" -c Release -v q \
  --artifacts-path "$work" -o "$work/helper" -p:GameDir="$game_dir" -p:RuntimeDir="$runtime_dir"
"$dotnet" publish "$here/src/AspectPatch/AspectPatch.csproj" -c Release -v q \
  --artifacts-path "$work" -o "$work/patch"

rm -rf "$out"
mkdir -p "$out"
cp "$work/helper/SlipstreamAspect.dll" "$out/"
cp "$work/patch/"{aspect-patch.dll,aspect-patch.runtimeconfig.json,Mono.Cecil.dll,Mono.Cecil.Rocks.dll} "$out/"
(cd "$out" && md5sum ./*)
