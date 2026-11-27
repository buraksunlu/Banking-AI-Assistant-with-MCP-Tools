#!/usr/bin/env bash
# The interactive presentation. By default one stage (DEMO-05): the presenter app (presentation/host) fills the screen with the
# slides and keeps a hole on the left where the simulator sits; with --safari the DEMO-04 layout (simulator left, Safari right).
# Either way presentation/server.py is the remote that turns a click on a slide into a question in the simulator. The API, MCP
# and AI services are not started here: run scripts/run-live.sh in another terminal first (the demo is on the live database).
#
#   scripts/present.sh            # the stage; ⌘Q in it ends the presentation
#   scripts/present.sh --safari   # the old two-window layout; Ctrl-C ends it
set -euo pipefail
cd "$(dirname "$0")/.."

URL="http://127.0.0.1:7300/"
APP="presentation/host/build/CompassPresenter.app"
mode="stage"
[[ "${1:-}" == "--safari" ]] && mode="safari"

if ! curl -fsS -m 2 http://localhost:5248/api/health > /dev/null 2>&1; then
  echo "present: the API is not answering on :5248; start scripts/run-live.sh in another terminal (the slides work, the answers will not)"
fi
if ! xcrun simctl list devices booted | grep -q Booted; then
  echo "present: no booted simulator; open the app from Xcode first (Debug build, signed in)"
fi

# Rebuilt only when a source changed: every build gets a new ad-hoc signature, and macOS then forgets the Accessibility permission
# (without it the stage still works, it only stops moving the simulator itself).
build_stage() {
  local binary="$APP/Contents/MacOS/CompassPresenter"
  if [[ -x "$binary" && -z "$(find presentation/host/Sources presentation/host/Package.swift presentation/host/Info.plist \
        -newer "$binary" -print -quit)" ]]; then
    return
  fi
  echo "present: building the stage app"
  swift build -c release --package-path presentation/host
  rm -rf "$APP"
  mkdir -p "$APP/Contents/MacOS"
  cp presentation/host/.build/release/CompassPresenter "$APP/Contents/MacOS/"
  cp presentation/host/Info.plist "$APP/Contents/"
  codesign --force --sign - "$APP"
}

[[ "$mode" == "stage" ]] && build_stage

python3 presentation/server.py & server=$!
# K2-D (DEMO-05 PR4a): the stage's AI service keeps the model in memory; when the stage quits it is unloaded (12.7 GB), the
# services keep running (K11-A) and the next question, or Başlat in the panel, loads it again.
trap 'kill "$server" 2>/dev/null || true; [[ "$mode" == "stage" ]] && { pkill -x CompassPresenter 2>/dev/null; scripts/stage.sh stop model > /dev/null; }; true' EXIT INT TERM
sleep 0.5
open -a Simulator

if [[ "$mode" == "stage" ]]; then
  echo "present: the stage is opening. Drag the simulator into the hole on the left if it does not jump there by itself"
  echo "present: (System Settings → Privacy & Security → Accessibility → CompassPresenter lets it)."
  echo "present: ←/→ slides · ⏎ next question · S phone follows slides · N notes · O map · ⌘Q ends"
  # -W waits until the stage quits; the trap then stops the remote.
  open -W -n "$APP" --args "$URL"
  exit 0
fi

open -a Safari "$URL"
sleep 1.5

# The main screen's visible area (menu bar excluded). Safari takes its bounds from its own dictionary; the simulator only through
# System Events, which needs this terminal to have Accessibility permission. Without it the windows stay where they are.
read -r left top right bottom < <(osascript -e 'tell application "Finder" to get bounds of window of desktop' | tr -d ',')
top=$((top + 25))
half=$(( (right - left) * 45 / 100 ))
osascript -e "tell application \"Safari\" to set bounds of front window to {$((left + half)), $top, $right, $bottom}" > /dev/null 2>&1 \
  || echo "present: could not place Safari; drag it to the right half"
osascript -e "tell application \"System Events\" to tell process \"Simulator\" to set position of window 1 to {$((left + 20)), $top}" \
  > /dev/null 2>&1 || echo "present: could not place the simulator (System Settings → Privacy & Security → Accessibility for this terminal)"

echo "present: $URL  ←/→ slides, N speaker notes; Ctrl-C stops the remote"
wait "$server"
