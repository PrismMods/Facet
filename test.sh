#!/bin/bash
# Offline tests: plain BCL code under Facet/Core only, no Unity.
set -e
cd "$(dirname "$0")"
out="${TMPDIR:-/tmp}/facettest.exe"
mcs -out:"$out" tests/FacetTest.cs Facet/Core/*.cs
last=$(mono "$out" | tee /dev/stderr | tail -n 1)
[ "$last" = "ALL PASS" ]
