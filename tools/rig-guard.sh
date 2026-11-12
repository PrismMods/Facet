#!/bin/bash
# Guards an in-game run on the player's own install. tools/ingame.sh calls each step; run alone for a dry check.
#   wait               refuse a locked screen (exit 4); wait WAIT_GAME s (default 1800) for a running game to exit (exit 3)
#   copies <dll>       list every Facet.0.2.dll that differs from <dll> and how to refresh them (exit 2)
#   pre                back up the settings files a run may touch; prints the backup directory
#   post <dir>         restore every backed-up file that changed, except the player's play stats
set -e
G="$HOME/Library/Application Support/Steam/steamapps/common/A Dance of Fire and Ice"
F="$(cd "$(dirname "$0")/.." && pwd)"
R="$(dirname "$F")"
KEEP="keycounts.txt layout.json KeyViewerLayout.json PlayCount.json CalibrationTimings.json"

game() { pgrep -x ADanceOfFireAndIce | head -n 1; }

case "$1" in
wait)
    if ioreg -n Root -d1 | grep -q '"CGSSessionScreenIsLocked"=Yes'; then echo "screen locked"; exit 4; fi
    pid=$(game); [ -z "$pid" ] && exit 0
    end=$((SECONDS + ${WAIT_GAME:-1800}))
    while pid=$(game); [ -n "$pid" ]; do
        if [ $SECONDS -ge $end ]; then echo "NEEDS_CONTEXT: a game is running, pid=$pid"; exit 3; fi
        sleep 30
    done
    sleep 5   # one game at a time, and a moment after the last one quit
    ;;
copies)
    [ -f "$2" ] || { echo "usage: $0 copies <built Facet.0.2.dll>" >&2; exit 1; }
    want=$(md5 -q "$2"); stale=0
    # UMMMods/unused is never loaded and no refresh reaches it.
    while IFS= read -r f; do
        [ "$(md5 -q "$f")" = "$want" ] || { echo "stale: $f"; stale=1; }
    done < <(find "$G/UMMMods" "$G/Mods" "$G/UserLibs" "$G/UserData/Quartz/Runtime" -name Facet.0.2.dll -not -path "$G/UMMMods/unused/*" 2>/dev/null)
    [ $stale = 0 ] && exit 0
    cat <<EOF
Within a minor the first-loaded copy wins: refresh every copy from this build, then run again.
  cd "$F" && ./test.sh && ./build.sh
  cd "$R/PrismLib-suite" && FACET_ROOT="$F" FORCE=1 tools/update-facet.sh && REPOS="../Topaz-suite ../Sapphire-suite ../CamDisplay-suite" ./dev.sh
  cp -R "$G/UserData/Quartz/Runtime" "\$HOME/QuartzRuntime-backup-\$(date +%Y%m%d-%H%M%S)"
  cd "$R/Quartz/Quartz-facet" && FACET_ROOT="$F" FORCE=1 tools/update-facet.sh && scripts/build.sh Debug ML
EOF
    exit 2
    ;;
pre)
    dir="$HOME/FacetRig-backup-$(date +%Y%m%d-%H%M%S)"
    mkdir -p "$dir/files"
    { [ -f "$G/Mods/PrismLib/settings.txt" ] && echo "$G/Mods/PrismLib/settings.txt"
      find "$G/UMMMods" "$G/UserData" -maxdepth 2 -type f \( -name '*.txt' -o -name '*.xml' -o -name '*.json' \) \
          ! -name '*Log*' ! -name '*.log' ! -name autotest ! -name selftest ! -path '*/Runtime/*'
    } | sort -u | while IFS= read -r f; do
        rel="${f#"$G/"}"
        mkdir -p "$dir/files/$(dirname "$rel")"
        cp -p "$f" "$dir/files/$rel"
        echo "$(md5 -q "$f") $rel" >> "$dir/md5.txt"
    done
    # UMMMods/PrismLib/settings.txt is at depth 2, so the find above took it.
    [ -d "$G/UMMMods/LayoutProbe" ] && echo yes > "$dir/layoutprobe" || echo no > "$dir/layoutprobe"
    echo "$dir"
    ;;
post)
    failed=; dir="$2"; [ -f "$dir/md5.txt" ] || { echo "usage: $0 post <dir from pre>" >&2; exit 1; }
    while read -r sum rel; do
        f="$G/$rel"
        [ -f "$f" ] && [ "$(md5 -q "$f")" = "$sum" ] && continue
        case " $KEEP " in *" $(basename "$rel") "*) echo "kept (play stats): $f"; continue ;; esac
        # One failed copy must not stop the rest: this runs from an EXIT trap.
        if mkdir -p "$(dirname "$f")" && cp -p "$dir/files/$rel" "$f"; then echo "restored: $f"
        else echo "NOT RESTORED: $f (backup: $dir/files/$rel)" >&2; failed=1; fi
    done < "$dir/md5.txt"
    if [ "$(cat "$dir/layoutprobe")" = no ] && [ -d "$G/UMMMods/LayoutProbe" ]; then
        rm -rf "$G/UMMMods/LayoutProbe"; echo "removed: $G/UMMMods/LayoutProbe (installed for this run only)"
    fi
    [ -z "$failed" ] || exit 1
    ;;
*) sed -n '2,6p' "$0" >&2; exit 1 ;;
esac
