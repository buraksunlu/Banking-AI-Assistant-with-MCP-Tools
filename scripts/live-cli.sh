#!/usr/bin/env bash
# Runs a CLI command against the live database (RT-02a), e.g. "scripts/live-cli.sh simulate catch-up --dry-run".
set -euo pipefail
cd "$(dirname "$0")/.."
source scripts/live-env.sh
exec dotnet run --project src/BankingAI.Cli -- "$@"
