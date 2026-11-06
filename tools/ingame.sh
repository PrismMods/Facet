#!/bin/bash
# Launch the game with the autotest flag, wait for the DONE line in gallery.log, report.
G="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"
D="$G/UMMMods/Facet"; LOG="$D/gallery.log"
if [ -e "$G/UMMMods/LayoutProbe/autotest" ] || [ -e "$G/UserData/CamDisplay/selftest" ]; then
  echo "another in-game suite is armed (LayoutProbe autotest or CamDisplay selftest); it would quit the game mid-run" >&2; exit 2
fi
osascript -e 'quit app "ADanceOfFireAndIce"' 2>/dev/null
sleep 5
caffeinate -dimsu -t 300 & CAF=$!
[ -d "$D" ] || { echo "run ./build.sh first" >&2; exit 2; }
rm -f "$LOG"; touch "$D/autotest"
open steam://rungameid/977950
cleanup() { rm -f "$D/autotest"; kill $CAF 2>/dev/null; }
trap cleanup EXIT
start=$SECONDS; last=$SECONDS; size=-1; rc=1
while :; do
  sleep 2
  s=$(stat -f %z "$LOG" 2>/dev/null || echo 0)
  if [ "$s" != "$size" ]; then size=$s; last=$SECONDS; fi
  grep -q "AUTOTEST DONE" "$LOG" 2>/dev/null && break
  if [ $((SECONDS-last)) -ge 90 ]; then echo "stalled: gallery.log unchanged for 90 s" >&2; break; fi
  if [ $((SECONDS-start)) -ge 300 ]; then echo "timeout after 300 s" >&2; break; fi
done
grep -E "FAIL|\[perf\]|AUTOTEST DONE" "$LOG" 2>/dev/null
grep -q "AUTOTEST DONE pass=[0-9]* fail=0$" "$LOG" 2>/dev/null && rc=0
exit $rc
