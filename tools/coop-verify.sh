#!/usr/bin/env bash
set -euo pipefail
source "$(dirname "$0")/env.sh"
cd "$AW_ROOT"
aw coop validate
aw coop demo
dotnet restore game/Ashenwake.Server --locked-mode --source "$AW_ROOT/.tools/feed" --disable-build-servers -p:NuGetAudit=false
dotnet restore game/Ashenwake.NetworkProbe --locked-mode --source "$AW_ROOT/.tools/feed" --disable-build-servers -p:NuGetAudit=false
dotnet build game/Ashenwake.NetworkProbe --no-restore --disable-build-servers -m:1
dotnet restore game/Ashenwake.Server.Tests --locked-mode --source "$AW_ROOT/.tools/feed" --disable-build-servers -p:NuGetAudit=false
dotnet test game/Ashenwake.Server.Tests --no-restore --disable-build-servers -m:1
bash tools/online-services-verify.sh verify
