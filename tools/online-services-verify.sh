#!/usr/bin/env bash
set -euo pipefail
umask 077
online_repo="$(cd "$(dirname "$0")/.." && pwd)"
export GOMODCACHE="${ASHENWAKE_ONLINE_MOD_CACHE:-/tmp/ashenwake-online-gomod}"
export GOCACHE="${ASHENWAKE_ONLINE_BUILD_CACHE:-/tmp/ashenwake-online-gocache}"
cd "$online_repo/services/online"
online_mode="${1:-verify}"
case "$online_mode" in
  verify)
    go mod verify
    go vet ./...
    go test -race -count=1 ./...
    ;;
  serve)
    # This path ALWAYS creates its own private cluster. An existing database URL
    # is deliberately replaced; callers cannot redirect this runner at user data.
    : "${ASHENWAKE_SERVER_KEY:?Set a private server key of at least 32 bytes}"
    unset PGSERVICE PGSERVICEFILE PGPASSFILE PGHOST PGHOSTADDR PGPORT PGDATABASE PGUSER PGPASSWORD PGOPTIONS
    online_pg_bin="${ASHENWAKE_TEST_PG_BIN:-$(pg_config --bindir)}"
    online_cluster="$(mktemp -d /tmp/ashenwake-online-serve.XXXXXX)"
    online_server_pid=""
    online_pg_started=0
    cleanup_online() {
      if [[ -n "$online_server_pid" ]] && kill -0 "$online_server_pid" 2>/dev/null; then
        kill -TERM "$online_server_pid"
        wait "$online_server_pid" || true
      fi
      if [[ "$online_pg_started" == 1 ]]; then
        "$online_pg_bin/pg_ctl" -D "$online_cluster/data" -m fast -w stop >"$online_cluster/stop.log" 2>&1 || true
      fi
      rm -rf "$online_cluster"
    }
    trap cleanup_online EXIT
    trap 'exit 130' INT
    trap 'exit 143' TERM
    go build -trimpath -o "$online_cluster/online" ./cmd/online
    "$online_pg_bin/initdb" -D "$online_cluster/data" -A trust --no-locale --encoding=UTF8 >"$online_cluster/init.log"
    "$online_pg_bin/pg_ctl" -D "$online_cluster/data" -l "$online_cluster/postgres.log" -o "-k $online_cluster -p 15438 -c listen_addresses='' -c log_statement=none -c log_min_error_statement=panic -c log_parameter_max_length_on_error=0" -w start >"$online_cluster/start.log"
    online_pg_started=1
    export ASHENWAKE_DATABASE_URL="host=$online_cluster port=15438 dbname=postgres user=$(id -un) sslmode=disable"
    export ASHENWAKE_HTTP_ADDR="${ASHENWAKE_HTTP_ADDR:-127.0.0.1:8088}"
    if [[ -z "${ASHENWAKE_GAME_SERVER_URL:-}" ]]; then
      export ASHENWAKE_GAME_SERVER_URL='ws://127.0.0.1:5180/v1/matches/{allocationId}/socket'
    fi
    "$online_cluster/online" &
    online_server_pid=$!
    wait "$online_server_pid"
    ;;
  *) printf '%s\n' 'usage: tools/online-services-verify.sh [verify|serve]' >&2; exit 2 ;;
esac
