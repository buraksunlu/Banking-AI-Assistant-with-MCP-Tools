#!/usr/bin/env bash
# The interactive presentation (DEMO-04): the simulator on the left half of the screen, the slides in Safari on the right, and the
# remote that turns a click on a slide into a question in the simulator (presentation/server.py). The API, MCP and AI services are
# not started here: run scripts/run-live.sh in another terminal first (the demo is on the live database).
set -euo pipefail
cd "$(dirname "$0")/.."

URL="http://127.0.0.1:7300/"

if ! curl -fsS -m 2 http://localhost:5248/api/health > /dev/null 2>&1; then
  echo "present: the API is not answering on :5248; start scripts/run-live.sh in another terminal (the slides work, the answers will not)"
fi
if ! xcrun simctl list devices booted | grep -q Booted; then
  echo "present: no booted simulator; open the app from Xcode first (Debug build, signed in)"
fi

python3 presentation/server.py & server=$!
trap 'kill "$server" 2>/dev/null || true' EXIT INT TERM
sleep 0.5

open -a Simulator
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
  > /dev/null 2>&1 || echo "present: could not place the simulator (System Settings → Privacy → Accessibility for this terminal)"

echo "present: $URL  ←/→ slides, N speaker notes; Ctrl-C stops the remote"
wait "$server"
