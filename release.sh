#!/bin/bash
# Stage the standalone mod for both loaders into dist/. Publishes nothing.
set -e
cd "$(dirname "$0")"
./test.sh
rm -rf dist; mkdir -p dist
dotnet build Facet/Facet.csproj -c Release --no-incremental
V=$(sed -n 's:.*<Version>\([0-9]*\.[0-9]*\)\..*:\1:p' Facet/Facet.csproj)
F=Facet/bin/Release/netstandard2.1/Facet.$V.dll
stage() { # name entries... ; stages dist/<name>/ from "src:dest" pairs, zips it, checks the listing
  local name=$1; shift; local want; want=$(for p in "$@"; do echo "${p#*:}"; done | sort)
  for p in "$@"; do mkdir -p "dist/$name/$(dirname "${p#*:}")"; cp "${p%%:*}" "dist/$name/${p#*:}"; done
  (cd "dist/$name" && zip -qr "../$name.zip" .)
  local got; got=$(unzip -Z1 "dist/$name.zip" | grep -v '/$' | sort)
  echo "== $name.zip"; echo "$got"
  [ "$got" = "$want" ] || { echo "$name.zip entries differ from the expected list" >&2; exit 1; }
}
dotnet build Facet.Mod/Facet.Mod.csproj -c Release -p:LoaderTarget=ML --no-incremental
stage Facet-ML "$F:UserLibs/Facet.$V.dll" "Facet.Mod/bin/ML/Facet.Mod.dll:Mods/Facet.Mod.dll"
dotnet build Facet.Mod/Facet.Mod.csproj -c Release --no-incremental
stage Facet-UMM "Facet.Mod/bin/Release/netstandard2.1/Facet.Mod.dll:Facet/Facet.Mod.dll" "$F:Facet/Facet.$V.dll" "Facet.Mod/Info.json:Facet/Info.json"
echo "release OK: dist/Facet-ML.zip dist/Facet-UMM.zip (nothing published)"
