#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null && [ -x /workspace/.dotnet/dotnet ]; then export PATH="/workspace/.dotnet:$PATH"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export ASPNETCORE_ENVIRONMENT=Development
export ASPNETCORE_URLS="${ASPNETCORE_URLS:-http://127.0.0.1:5080}"
export Storage__Root="${Storage__Root:-$(pwd)/data}"
exec dotnet run --no-launch-profile --project src/VideoAtlas.Web
