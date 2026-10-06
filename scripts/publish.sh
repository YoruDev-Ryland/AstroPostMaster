#!/usr/bin/env bash
# Builds self-contained single-file AstroPostMaster binaries (no .NET install needed to run them).
# Usage: scripts/publish.sh [rid ...]      default: linux-x64 win-x64 osx-arm64 osx-x64
set -euo pipefail
cd "$(dirname "$0")/.."
command -v dotnet >/dev/null || export PATH="$HOME/.dotnet:$PATH"

rids=("$@")
[[ ${#rids[@]} -eq 0 ]] && rids=(linux-x64 win-x64 osx-arm64 osx-x64)

for rid in "${rids[@]}"; do
    out="publish/$rid"
    echo "==> $rid"
    dotnet publish src/AstroPostMaster.App/AstroPostMaster.App.csproj \
        -c Release -r "$rid" --self-contained \
        -p:PublishSingleFile=true \
        -p:IncludeNativeLibrariesForSelfExtract=true \
        -p:EnableCompressionInSingleFile=true \
        -p:DebugType=none \
        -o "$out" --nologo -v quiet
    rm -f "$out"/*.pdb   # native debug symbols shipped by SkiaSharp/HarfBuzz packages
    (cd "$out" && rm -f "../AstroPostMaster-$rid.zip" && zip -q -9 "../AstroPostMaster-$rid.zip" ./*)
    ls -lh "$out" | sed 1d
    ls -lh "publish/AstroPostMaster-$rid.zip" | awk '{print "zip: " $5 " " $9}'
done
