#!/usr/bin/env bash
# The stage's setup panel (DEMO-05 PR2): starts, stops and checks every part the presentation needs, one fixed name at a time.
# presentation/server.py runs it with two fixed words (K8-A); it works the same from a terminal.
#
#   scripts/stage.sh status                                      one line of JSON: db, model, api, mcp, ai, simulator, app
#                                                                (model: off, missing, cold or on = in memory;
#                                                                 app: off, missing, old, installed or running)
#   scripts/stage.sh start db|model|services|simulator|app|all
#   scripts/stage.sh stop services|model|all                     the database stays up (K11-A)
#   scripts/stage.sh install app                                 Debug build of the iPhone app, installed and opened
#
# The services run on the live database (K9-A, scripts/live-env.sh), as scripts/run-live.sh does; started here they keep running
# after the stage quits, and `stop` ends them. The steps are those of scripts/demo.sh, which keeps the fixed demo database.
# DEMO-05 PR4a: `start model` also loads the model, and the AI service started here never unloads it (K2-D): a model idle for
# longer than its 30 minutes timed the first question out (504) while the light was green. present.sh unloads it when the stage
# quits. Sourced (presentation/test_stage.py), the script only defines its functions.
set -uo pipefail
cd "$(dirname "${BASH_SOURCE[0]}")/.."

export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="$DOTNET_ROOT:/opt/homebrew/bin:/usr/local/bin:$PATH"
# The database lives in Colima; Docker Desktop switches the active context back to itself when it starts.
export DOCKER_CONTEXT=colima
PG_CONTAINER=bankingai-week1-postgres-1
MODEL="${BANKINGAI_LLM_MODEL:-qwen3.5:4b}"
# Models live on the external SSD; a bare `ollama serve` does not know that location and lists no models.
MODELS_DIR="${OLLAMA_MODELS:-/Volumes/burakSSD/Ollama/models}"
OLLAMA_URL="${OLLAMA_URL:-http://127.0.0.1:11434}"
DEVICE="iPhone 17 Pro"
BUNDLE=com.bankingai.BankingAI
BUILD=.demo/stage-build
LOGS=logs
mkdir -p "$LOGS"

say() { echo "stage: $*"; }
fail() { echo "stage: $*"; exit 1; }

# Output is captured before grep: `grep -q` closes a pipe early and pipefail would then report a running part as down.
db_on() { docker exec "$PG_CONTAINER" pg_isready -q > /dev/null 2>&1; }
model_installed() { local tags; tags=$(curl -fs -m 1 "$OLLAMA_URL/api/tags" 2>/dev/null) && grep -qF "\"$MODEL\"" <<< "$tags"; }
# /api/tags lists what is installed, /api/ps what is in memory: only the second answers a question without loading first.
model_state() {
  local tags loaded
  tags=$(curl -fs -m 1 "$OLLAMA_URL/api/tags" 2>/dev/null) || { echo off; return; }
  grep -qF "\"$MODEL\"" <<< "$tags" || { echo missing; return; }
  loaded=$(curl -fs -m 1 "$OLLAMA_URL/api/ps" 2>/dev/null) && grep -qF "\"$MODEL\"" <<< "$loaded" && echo on || echo cold
}
api_on() { curl -fs -m 1 -o /dev/null http://localhost:5248/api/health; }
mcp_on() { lsof -iTCP:5249 -sTCP:LISTEN > /dev/null 2>&1; }
ai_on() { curl -fs -m 1 -o /dev/null http://127.0.0.1:8000/health/ready; }
simulator_on() { local devices; devices=$(xcrun simctl list devices booted 2>/dev/null) && grep -q Booted <<< "$devices"; }
app_installed() { xcrun simctl get_app_container booted "$BUNDLE" > /dev/null 2>&1; }
# An app built before DEMO-04 has no compass:// scheme and every slide button fails with "error 115" (found 27 November).
app_current() {
  local container types
  container=$(xcrun simctl get_app_container booted "$BUNDLE" app 2>/dev/null) || return 1
  types=$(plutil -extract CFBundleURLTypes json -o - "$container/Info.plist" 2>/dev/null) && grep -qF '"compass"' <<< "$types"
}
app_running() { local list; list=$(xcrun simctl spawn booted launchctl list 2>/dev/null) && grep -qF "UIKitApplication:$BUNDLE" <<< "$list"; }

# name url seconds
wait_for() {
  for _ in $(seq 1 "$3"); do
    curl -fs -m 1 -o /dev/null "$2" && return 0
    sleep 1
  done
  fail "$1 did not answer on $2 within $3 s (log: $LOGS/live-*.log)"
}

status() {
  local app="missing"
  if ! simulator_on; then app="off"
  elif ! app_installed; then app="missing"
  elif ! app_current; then app="old"
  elif app_running; then app="running"
  else app="installed"
  fi
  on() { "$1" && echo on || echo off; }
  printf '{"db":"%s","model":"%s","api":"%s","mcp":"%s","ai":"%s","simulator":"%s","app":"%s"}\n' \
    "$(on db_on)" "$(model_state)" "$(on api_on)" "$(on mcp_on)" "$(on ai_on)" "$(on simulator_on)" "$app"
}

start_db() {
  db_on && { say "database already up"; return; }
  say "starting Colima and PostgreSQL"
  colima status > /dev/null 2>&1 || colima start || fail "Colima did not start"
  docker start "$PG_CONTAINER" > /dev/null || fail "could not start $PG_CONTAINER"
  for _ in $(seq 1 30); do db_on && { say "database up"; return; }; sleep 1; done
  fail "PostgreSQL is not ready"
}

# No prompt: Ollama only loads the model (AI-15). -1 as a number keeps it until `stop model`; the text "-1" is refused.
warm_model() {
  curl -fs -m 120 -o /dev/null -H 'Content-Type: application/json' -d "{\"model\":\"$MODEL\",\"keep_alive\":-1}" \
    "$OLLAMA_URL/api/generate" || fail "could not load $MODEL into memory"
}

start_model() {
  if ! model_installed; then
    [[ -d "$MODELS_DIR" ]] || fail "$MODELS_DIR is missing (is the external SSD plugged in?)"
    if ! curl -fs -m 1 -o /dev/null "$OLLAMA_URL/api/tags"; then
      say "starting Ollama"
      OLLAMA_MODELS="$MODELS_DIR" nohup ollama serve > "$LOGS/stage-ollama.log" 2>&1 &
    fi
    wait_for "Ollama" "$OLLAMA_URL/api/tags" 20
    model_installed || fail "$MODEL is not installed in $MODELS_DIR"
  fi
  # Also when it is in memory already: a model loaded by an earlier service still has its 30 minutes, this makes it stay.
  say "loading $MODEL into memory"
  warm_model
  say "model in memory until unloaded"
  if ai_on; then warm_chain; fi
}

# Loading is not enough (measured): the first questions after it took ~25 s each. Four real questions as Murat, signed locally
# with the .env key the way the live trial does; none of it is printed but the times and the kinds of reply.
warm_chain() {
  say "warming the AI service: four questions (up to two minutes)"
  ai/.venv/bin/python presentation/live_trial.py --warm || fail "the warm-up questions got no answer (log: $LOGS/live-ai.log)"
  say "model in memory and warmed"
}

start_ai() {
  BANKINGAI_LLM_KEEP_ALIVE=-1 nohup ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000 \
    > "$LOGS/live-ai.log" 2>&1 &
}

start_services() {
  db_on || fail "the database is down; start it first"
  # shellcheck source=scripts/live-env.sh
  source scripts/live-env.sh || fail "the live database settings were not found"
  api_on || { say "starting the API"; nohup dotnet run --project src/BankingAI.Api --launch-profile http > "$LOGS/live-api.log" 2>&1 & }
  mcp_on || { say "starting the MCP server"; nohup dotnet run --project src/BankingAI.McpServer --launch-profile http > "$LOGS/live-mcp.log" 2>&1 & }
  if ! curl -fs -m 1 -o /dev/null http://127.0.0.1:8000/health/live; then
    say "starting the AI service"
    start_ai
  fi
  wait_for "the API" http://localhost:5248/api/health 90
  wait_for "the AI service" http://127.0.0.1:8000/health/live 60
  for _ in $(seq 1 30); do mcp_on && break; sleep 1; done
  mcp_on || fail "the MCP server did not start (log: $LOGS/live-mcp.log)"
  wait_for "the AI service with its model" http://127.0.0.1:8000/health/ready 90
  say "services up on the live database"
}

start_simulator() {
  if ! simulator_on; then
    say "booting $DEVICE"
    xcrun simctl boot "$DEVICE" || fail "could not boot $DEVICE"
  fi
  open -a Simulator
  say "simulator up"
}

start_app() {
  simulator_on || start_simulator
  app_installed || fail "the app is not installed: use Build and install"
  xcrun simctl launch booted "$BUNDLE" > /dev/null || fail "the app did not open"
  say "app open; sign in on the phone if it asks"
}

install_app() {
  simulator_on || start_simulator
  say "building the iPhone app (Debug; a few minutes the first time)"
  xcodebuild -project ios/BankingAI/BankingAI.xcodeproj -scheme BankingAI -configuration Debug \
    -destination 'generic/platform=iOS Simulator' -derivedDataPath "$BUILD" -quiet build > "$LOGS/stage-xcodebuild.log" 2>&1 \
    || fail "the build failed (log: $LOGS/stage-xcodebuild.log)"
  local app
  app=$(find "$BUILD/Build/Products/Debug-iphonesimulator" -maxdepth 1 -name '*.app' | head -1)
  [[ -n "$app" ]] || fail "the build made no app"
  xcrun simctl install booted "$app" || fail "could not install the app"
  start_app
}

stop_services() {
  for port in 5248 5249 8000; do
    pids=$(lsof -tiTCP:$port -sTCP:LISTEN 2>/dev/null)
    [[ -n "$pids" ]] && kill $pids 2>/dev/null
  done
  say "services stopped"
}

stop_model() {
  ollama stop "$MODEL" > /dev/null 2>&1
  say "model unloaded"
}

main() {
case "${1:-} ${2:-}" in
  "status ") status ;;
  "start db") start_db ;;
  "start model") start_model ;;
  "start services") start_services ;;
  "start simulator") start_simulator ;;
  "start app") start_app ;;
  "start all")
    # The model is loaded before the services and warmed once they answer (warm_chain needs the AI service).
    start_db && start_model && start_services && warm_chain && start_simulator
    if app_installed && app_current; then start_app; else install_app; fi
    say "all up"
    ;;
  "install app") install_app ;;
  "stop services") stop_services ;;
  "stop model") stop_model ;;
  "stop all") stop_services; stop_model; say "all stopped (the database stays up)" ;;
  *) echo "usage: scripts/stage.sh status | start db|model|services|simulator|app|all | stop services|model|all | install app" >&2; exit 2 ;;
esac
}

# An `if`, not `&&`: sourced, a false test would end the file with status 1 and stop the caller's `source … &&`.
if [[ "${BASH_SOURCE[0]}" == "$0" ]]; then main "$@"; fi
