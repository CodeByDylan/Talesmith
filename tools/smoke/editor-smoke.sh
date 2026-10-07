#!/usr/bin/env bash
# Drives the editor in a real X11 window under Xvfb and saves a screenshot after each step.
# Needs dotnet, xvfb-run, xdotool and ImageMagick's import, for example:
#   nix shell nixpkgs#xvfb-run nixpkgs#xdotool nixpkgs#imagemagick --command tools/smoke/editor-smoke.sh
# Usage: editor-smoke.sh [output folder] [template]   (template: empty, hex-adventure or platformer)
set -euo pipefail

repo="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
out="$(realpath -m "${1:-$repo/artifacts/smoke}")"
template="${2:-platformer}"
app="$repo/src/Talesmith.App/bin/Debug/net10.0/talesmith.dll"

if [[ -z "${SMOKE_INNER:-}" ]]; then
  [[ -f "$app" ]] || dotnet build "$repo/src/Talesmith.App" -v q
  mkdir -p "$out"
  rm -f "$out"/*.png "$out"/editor.log
  unset WAYLAND_DISPLAY
  SMOKE_INNER=1 exec xvfb-run -a -s "-screen 0 1600x1000x24" "$0" "$out" "$template"
fi

work="$(mktemp -d)"
trap 'kill "$editor" 2>/dev/null || true; rm -rf "$work"' EXIT
export HOME="$work/home" XDG_CONFIG_HOME="$work/home/.config"
mkdir -p "$XDG_CONFIG_HOME"

step=0
shot() {
  step=$((step + 1))
  sleep "${2:-1}"
  import -window root "$(printf '%s/%02d-%s.png' "$out" "$step" "$1")"
  echo "  $(printf '%02d' "$step") $1"
}

fail() {
  echo "FAILED: $1" >&2
  tail -n 40 "$out/editor.log" >&2 || true
  exit 1
}

alive() { kill -0 "$editor" 2>/dev/null || fail "the editor exited (see $out/editor.log)"; }

echo "Creating a $template project and opening it"
dotnet "$app" --new "$template" "$work/projects" "Smoke Test" > "$out/editor.log" 2>&1 &
editor=$!

window=""
for _ in $(seq 1 120); do
  alive
  window="$(xdotool search --name 'Smoke Test' 2>/dev/null | head -n 1 || true)"
  [[ -n "$window" ]] && break
  sleep 0.5
done
[[ -n "$window" ]] || fail "no editor window appeared"
sleep 2
xdotool windowsize "$window" 1600 1000 windowmove "$window" 0 0
xdotool windowfocus --sync "$window" 2>/dev/null || true
sleep 8
alive
eval "$(xdotool getwindowgeometry --shell "$window")"
shot opened

# Window-relative positions in the default layout: hierarchy rows on the left, the viewport in the middle.
at() { xdotool mousemove $((X + $1)) $((Y + $2)); }
hero_row=(60 223)
viewport=(700 360)

echo "Selecting an entity and moving it with the Move tool"
at "${hero_row[@]}"
xdotool click 1
shot selected
xdotool key f
sleep 1
xdotool key w
shot move-tool 1.5
at "${viewport[@]}"
xdotool mousedown 1
for i in $(seq 1 12); do at $((viewport[0] + i * 10)) $((viewport[1] - i * 5)); sleep 0.03; done
xdotool mouseup 1
shot dragged
alive

echo "Undoing the move"
xdotool key ctrl+z
shot undone

echo "Playing and stopping"
xdotool key ctrl+p
shot playing 5
alive
xdotool key ctrl+p
shot stopped 2
alive

echo "Searching commands"
xdotool key ctrl+k
# Keys sent before the palette is open go to the editor instead, and the late palette then swallows Ctrl+Q.
sleep 2
xdotool type --delay 60 "grid"
shot palette
xdotool key Escape
shot palette-closed
alive

echo "Closing the editor"
xdotool key ctrl+q
for _ in $(seq 1 120); do
  kill -0 "$editor" 2>/dev/null || break
  sleep 0.25
done
if kill -0 "$editor" 2>/dev/null; then
  shot close-prompt
  fail "the editor did not close"
fi
wait "$editor" && code=0 || code=$?
grep -E 'free\(\)|SIGSEGV|Unhandled exception|core dumped' "$out/editor.log" && fail "the log shows a crash"
[[ $code -eq 0 ]] || fail "the editor exited with code $code"
echo "Passed; screenshots in $out"
