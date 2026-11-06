#!/bin/bash
set -e
cd "$(dirname "$0")"
./test.sh
dotnet build Facet/Facet.csproj -c Release
[ -f Facet/bin/Release/netstandard2.0/Facet.0.1.dll ] || { echo "missing Facet.0.1.dll" >&2; exit 1; }
