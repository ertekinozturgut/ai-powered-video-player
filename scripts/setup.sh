#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
if ! command -v dotnet >/dev/null && [ -x /workspace/.dotnet/dotnet ]; then export PATH="/workspace/.dotnet:$PATH"; fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
dotnet --version
ffmpeg -version
ffprobe -version
dotnet restore VideoAtlas.sln --locked-mode
npm ci
npm run assets
dotnet build VideoAtlas.sln --no-restore
dotnet test VideoAtlas.sln --no-build --logger 'console;verbosity=minimal'
