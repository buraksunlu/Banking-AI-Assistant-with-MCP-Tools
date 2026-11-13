#!/usr/bin/env bash
# Starts (or stops) every service the demo needs: Colima + PostgreSQL, Ollama, .NET API, MCP server, AI service.
#   scripts/demo.sh          start everything, wait until healthy, keep running (Ctrl+C stops the three app services)
#   scripts/demo.sh stop     stop the app services and unload the model
set -uo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
LOGS="$ROOT/.demo"
mkdir -p "$LOGS"

export DOTNET_ROOT=/usr/local/share/dotnet
export PATH="$DOTNET_ROOT:/opt/homebrew/bin:/usr/local/bin:$PATH"
PG_CONTAINER=bankingai-week1-postgres-1
# The database lives in Colima; Docker Desktop switches the active context back to itself when it starts.
export DOCKER_CONTEXT=colima
DEMO_NOW="2026-10-10T12:00:00Z"

stop_services() {
  for port in 5248 5249 8000; do
    pids=$(lsof -tiTCP:$port -sTCP:LISTEN 2>/dev/null)
    [ -n "$pids" ] && kill $pids 2>/dev/null
  done
  sleep 1
}

if [ "${1:-}" = "stop" ]; then
  stop_services
  ollama stop "${BANKINGAI_LLM_MODEL:-qwen3.5:4b}" >/dev/null 2>&1
  echo "Servisler durduruldu, model bellekten bosaltildi. (PostgreSQL/Colima acik birakildi.)"
  exit 0
fi

[ -f .env ] || { echo "HATA: .env yok (.env.example'dan kopyala)."; exit 1; }
set -a; source .env; set +a

wait_for() { # name url seconds
  for _ in $(seq 1 "$3"); do
    curl -fs -o /dev/null "$2" && { echo "  ok  $1"; return 0; }
    sleep 1
  done
  echo "  HATA  $1 acilmadi ($2); log: $LOGS"; return 1
}

echo "1/5 PostgreSQL"
colima status >/dev/null 2>&1 || colima start
docker start "$PG_CONTAINER" >/dev/null
for _ in $(seq 1 30); do
  docker exec "$PG_CONTAINER" pg_isready -q && break
  sleep 1
done
docker exec "$PG_CONTAINER" pg_isready || { echo "HATA: PostgreSQL hazir degil."; exit 1; }

echo "2/5 Ollama"
# Models live on the external SSD; a bare `ollama serve` does not know that location and lists no models.
OLLAMA_MODELS_DIR="${OLLAMA_MODELS:-/Volumes/burakSSD/Ollama/models}"
[ -d "$OLLAMA_MODELS_DIR" ] || { echo "HATA: $OLLAMA_MODELS_DIR yok (harici SSD takili mi?)"; exit 1; }
if ! curl -fs -o /dev/null http://127.0.0.1:11434/api/tags; then
  OLLAMA_MODELS="$OLLAMA_MODELS_DIR" nohup ollama serve >"$LOGS/ollama.log" 2>&1 &
fi
wait_for "ollama" http://127.0.0.1:11434/api/tags 20 || exit 1
# Capture first: `grep -q` closes the pipe early and pipefail would report a false failure.
INSTALLED_MODELS="$(ollama list)"
grep -qF "${BANKINGAI_LLM_MODEL:-qwen3.5:4b}" <<<"$INSTALLED_MODELS" \
  || { echo "HATA: model yok (harici SSD takili mi?)"; exit 1; }

echo "3/5 Derleme"
dotnet build src/BankingAI.Api -v q --nologo || exit 1
dotnet build src/BankingAI.McpServer -v q --nologo || exit 1

stop_services
echo "4/5 Servisler (demo aninda: $DEMO_NOW)"
BANKINGAI_DEMO_NOW="$DEMO_NOW" nohup dotnet run --no-build --project src/BankingAI.Api --launch-profile http >"$LOGS/api.log" 2>&1 &
BANKINGAI_DEMO_NOW="$DEMO_NOW" nohup dotnet run --no-build --project src/BankingAI.McpServer --launch-profile http >"$LOGS/mcp.log" 2>&1 &
nohup ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000 >"$LOGS/ai.log" 2>&1 &

echo "5/5 Saglik kontrolu"
wait_for "api  :5248" http://localhost:5248/api/health 40 || exit 1
wait_for "ai   :8000" http://127.0.0.1:8000/health/live 40 || exit 1
wait_for "ai hazir (model)" http://127.0.0.1:8000/health/ready 60 || echo "  uyari: model henuz hazir degil"
lsof -iTCP:5249 -sTCP:LISTEN >/dev/null 2>&1 && echo "  ok  mcp :5249" || echo "  HATA  mcp acilmadi; log: $LOGS/mcp.log"

echo
echo "Hazir. Loglar: $LOGS  |  Scalar: http://localhost:5248/scalar  |  Durdur: scripts/demo.sh stop"
