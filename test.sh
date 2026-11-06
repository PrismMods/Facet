#!/bin/bash
# Offline tests: plain BCL code under Facet/Core only, no Unity.
set -e
cd "$(dirname "$0")"
out="${TMPDIR:-/tmp}/facettest.exe"
mcs -out:"$out" tests/FacetTest.cs Facet/Core/*.cs
last=$(mono "$out" | tee /dev/stderr | tail -n 1)
[ "$last" = "ALL PASS" ]
tests/shared2.sh
if grep -rnE 'RDInput|Assembly-CSharp|UnityModManager|MelonLoader|Harmony|scnEditor|ADOFAI' Facet/ --include='*.cs'; then
    echo "game knowledge in Facet/"; exit 1; fi
if grep -rn 'RegisterCallback\|ConditionalWeakTable' Facet/ --include='*.cs' | grep -v 'Events.cs\|Data.cs'; then
    echo "RegisterCallback/ConditionalWeakTable outside Events.cs/Data.cs"; exit 1; fi
if grep -rnE 'class \w+ *: *(VisualElement|Button|Label|TextField|ScrollView|MonoBehaviour)' Facet/ --include='*.cs'; then
    echo "a Facet type derives from a Unity type (IL2CPP rule 1)"; exit 1; fi
