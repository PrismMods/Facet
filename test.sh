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
# Colour is painted by role (Paint.cs); only the pickers' swatches may write a literal.
if grep -rnE 'style\.(backgroundColor|color|border[A-Za-z]*Color|unity[A-Za-z]*Color) *=' Facet/ --include='*.cs' | grep -v 'Paint.cs\|ColourPicker.cs\|GradientEditor.cs'; then
    echo "direct colour write outside Paint.cs and the pickers: paint by role"; exit 1; fi
# Sizes and looks come from the part's theme (Theme.For(e).Sizes / .Looks), never a literal or a preset.
if grep -rnE 'SetRadius\([^,]+, *([1-9][0-9.]*|0\.[0-9]+)f?\)|fontSize *= *[0-9]' Facet/ --include='*.cs'; then
    echo "a size literal in a part: read it from the theme's Sizes"; exit 1; fi
if grep -rnE '\bLook\.(Button|FlatButton|AccentButton|DangerButton|Row|RailItem|Field|Ring|Card|Chrome)\b' Facet/ --include='*.cs' | grep -v 'Core/Looks.cs'; then
    echo "a part paints a Look preset: take it from the theme's Looks"; exit 1; fi
