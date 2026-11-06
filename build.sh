#!/bin/bash
set -e
cd "$(dirname "$0")"
./test.sh
dotnet build Facet/Facet.csproj -c Release
V=$(sed -n 's:.*<Version>\([0-9]*\.[0-9]*\)\..*:\1:p' Facet/Facet.csproj)
[ -f Facet/bin/Release/netstandard2.1/Facet.$V.dll ] || { echo "missing Facet.$V.dll" >&2; exit 1; }
G="${ADOFAI_ROOT:-$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice}"
dotnet build Facet.Mod/Facet.Mod.csproj -c Release
mkdir -p "$G/UMMMods/Facet"
cp Facet.Mod/bin/Release/netstandard2.1/Facet.Mod.dll Facet.Mod/Info.json Facet/bin/Release/netstandard2.1/Facet.$V.dll "$G/UMMMods/Facet/"
rm -f "$G/UMMMods/Facet/gallery.log"
