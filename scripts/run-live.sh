#!/usr/bin/env bash
# Runs the API, the MCP server and the AI service against the live database (RT-01 D3-C). The tests and the model measurements keep
# the fixed demo database of .env (bankingai_foundation); this script never touches it.
set -euo pipefail
cd "$(dirname "$0")/.."

export DOTNET_ROOT=/usr/local/share/dotnet PATH="/usr/local/share/dotnet:$PATH"
set -a; source .env; set +a

# The live connection: its own setting when .env has one, otherwise the demo connection with the live database's name.
live="${BANKINGAI_LIVE_CONNECTION_STRING:-${BANKINGAI_CONNECTION_STRING/Database=bankingai_foundation/Database=bankingai_live}}"
case "$live" in
  *Database=bankingai_live*) ;;
  *) echo "run-live: could not find the live database in the connection settings" >&2; exit 1 ;;
esac
export BANKINGAI_CONNECTION_STRING="$live"

mkdir -p logs
pids=()
trap 'kill "${pids[@]}" 2>/dev/null || true' EXIT INT TERM
dotnet run --project src/BankingAI.Api --launch-profile http > logs/live-api.log 2>&1 & pids+=($!)
dotnet run --project src/BankingAI.McpServer --launch-profile http > logs/live-mcp.log 2>&1 & pids+=($!)
ai/.venv/bin/uvicorn --app-dir ai/service --factory app:create_app --host 127.0.0.1 --port 8000 > logs/live-ai.log 2>&1 & pids+=($!)
echo "run-live: API :5248, MCP :5249, AI :8000 on bankingai_live (logs/live-*.log); Ctrl-C stops all three"
wait
