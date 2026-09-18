#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
umask 077
mkdir -p artifacts/coop-network
network_output="$(mktemp -d "$AW_ROOT/artifacts/coop-network/run.XXXXXX")"
# Credentials exist only in this process environment and its children.
export ASHENWAKE_SERVER_KEY="$(python3 -c 'import secrets; print(secrets.token_hex(32))')"
export ASHENWAKE_GAME_SERVER_URL='ws://127.0.0.1:5180/v1/matches/{allocationId}/socket'
export ASHENWAKE_CONTROL_URL='http://127.0.0.1:8088/'
export ASHENWAKE_LISTEN='http://127.0.0.1:5180'
python3 - <<'PY'
import socket
for port in (8088,5180):
    with socket.socket() as s:
        try:s.bind(('127.0.0.1',port))
        except OSError:raise SystemExit(f'Local test port {port} is occupied; existing services were not touched.')
PY
bash tools/online-services-verify.sh serve > "$network_output/control.log" 2>&1 &
network_control_pid=$!
cleanup_network() {
    if kill -0 "$network_control_pid" 2>/dev/null; then kill -TERM "$network_control_pid"; wait "$network_control_pid" || true; fi
}
trap cleanup_network EXIT
trap 'exit 130' INT
trap 'exit 143' TERM
python3 - <<'PY'
import time,urllib.request
for _ in range(200):
    try:
        with urllib.request.urlopen('http://127.0.0.1:8088/readyz',timeout=.5) as r:
            if r.status==200:break
    except OSError:time.sleep(.1)
else:raise SystemExit('Private control service did not become ready.')
PY
dotnet game/Ashenwake.NetworkProbe/bin/Debug/net8.0/Ashenwake.NetworkProbe.dll run "$network_output/probe"
