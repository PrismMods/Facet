#!/bin/bash
# Launch the game with the autotest flag, wait for the DONE line in gallery.log, report.
# The player's game and settings come first (tools/rig-guard.sh): never quits a game; exit 3 = NEEDS_CONTEXT.
G="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"
F="$(cd "$(dirname "$0")/.." && pwd)"
GUARD="$F/tools/rig-guard.sh"
# --ml watches the MelonLoader entry (UserData/Facet); default is the UMM install (UMMMods/Facet).
if [ "$1" = "--ml" ]; then D="$G/UserData/Facet"; INST="$G/Mods/Facet.Mod.dll"; else D="$G/UMMMods/Facet"; INST="$D"; fi
LOG="$D/gallery.log"
if [ -e "$G/UMMMods/LayoutProbe/autotest" ] || [ -e "$G/UserData/CamDisplay/selftest" ]; then
  echo "another in-game suite is armed (LayoutProbe autotest or CamDisplay selftest); it would quit the game mid-run" >&2; exit 2
fi
[ -e "$INST" ] || { echo "not installed: run ./build.sh (UMM) or install dist/Facet-ML.zip (--ml)" >&2; exit 2; }
"$GUARD" wait || exit $?
"$GUARD" copies "$F/Facet/bin/Release/netstandard2.1/Facet.0.2.dll" || exit $?
BAK=$("$GUARD" pre) || exit $?
echo "backup: $BAK"
caffeinate -dimsu -w $$ & CAF=$!
mkdir -p "$D"
rm -f "$LOG"; touch "$D/autotest"
cleanup() {
  rm -f "$D/autotest"; kill $CAF 2>/dev/null; wait $CAF 2>/dev/null
  "$GUARD" post "$BAK"
  # The game writes its settings again when it quits, so a restore made while it runs may not last.
  p=$(pid); [ -n "$p" ] && echo "the game is still running (pid=$p): once it exits, run: $GUARD post '$BAK'"
}
pid() { pgrep -x ADanceOfFireAndIce | head -n 1; }
trap cleanup EXIT
open steam://rungameid/977950
start=$SECONDS; last=$SECONDS; size=-1; rc=1; hinted=
while :; do
  sleep 2
  s=$(stat -f %z "$LOG" 2>/dev/null || echo 0)
  [ "$s" = 0 ] && last=$SECONDS   # no log yet: only the total cap applies
  if [ "$s" = 0 ] && [ -z "$hinted" ] && [ $((SECONDS-start)) -ge 45 ]; then
    echo "no log yet: check Steam's cloud dialog (choose LOCAL) and Screen Time (Ignore Limit)" >&2; hinted=1
  fi
  if [ "$s" != "$size" ]; then size=$s; last=$SECONDS; fi
  grep -q "AUTOTEST DONE" "$LOG" 2>/dev/null && break
  if [ $((SECONDS-last)) -ge 90 ] || [ $((SECONDS-start)) -ge 300 ]; then
    p=$(pid)
    [ -n "$p" ] && { grep -E "FAIL|\[perf\]" "$LOG" 2>/dev/null; echo "NEEDS_CONTEXT: launch hung, pid=$p"; exit 3; }
    echo "the game exited without AUTOTEST DONE" >&2; break
  fi
done
grep -E "FAIL|\[perf\]|AUTOTEST DONE" "$LOG" 2>/dev/null
grep -q "AUTOTEST DONE pass=[0-9]* fail=0$" "$LOG" 2>/dev/null && rc=0
if grep -q "AUTOTEST DONE" "$LOG" 2>/dev/null; then
  # The autotest quits the game itself; post must not restore files the game is still writing.
  for _ in $(seq 30); do [ -z "$(pid)" ] && break; sleep 2; done
  p=$(pid); [ -n "$p" ] && { echo "NEEDS_CONTEXT: game did not exit, pid=$p"; exit 3; }
fi
exit $rc
