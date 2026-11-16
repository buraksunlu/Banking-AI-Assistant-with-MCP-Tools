# Sourced by the live scripts (RT-01 D3-C): loads .env and points BANKINGAI_CONNECTION_STRING at the live database, from its own
# setting when .env has one, otherwise from the demo connection with the live database's name. The fixed database is never used.
export DOTNET_ROOT=/usr/local/share/dotnet PATH="/usr/local/share/dotnet:$PATH"
set -a; source .env; set +a
live="${BANKINGAI_LIVE_CONNECTION_STRING:-${BANKINGAI_CONNECTION_STRING/Database=bankingai_foundation/Database=bankingai_live}}"
case "$live" in
  *Database=bankingai_live*) ;;
  *) echo "live: could not find the live database in the connection settings" >&2; exit 1 ;;
esac
export BANKINGAI_CONNECTION_STRING="$live"
