#!/usr/bin/env bash
set -euo pipefail
cd /workspace/ai-powered-video-player
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export PATH="/workspace/.dotnet:$PATH"
export DOTNET_ROOT=/workspace/.dotnet
if [ ! -x /workspace/.dotnet/dotnet ] || [ "$(/workspace/.dotnet/dotnet --version)" != '10.0.401' ]; then
    setup_tmp="$(mktemp -d /tmp/videoatlas-sdk.XXXXXX)"
    curl -fL https://builds.dotnet.microsoft.com/dotnet/Sdk/10.0.401/dotnet-sdk-10.0.401-linux-x64.tar.gz -o "$setup_tmp/sdk.tar.gz"
    printf '%s  %s\n' '51c8b999af9e8dd9998c9edc5944e19a90788862068acd38694e098889054ce8c23d4f0c5cccfa16bf187d044562359e5ee69a9f8ad0bbe913ba90311fbce25b' "$setup_tmp/sdk.tar.gz" | sha512sum -c -
    mkdir -p /workspace/.dotnet
    tar -xzf "$setup_tmp/sdk.tar.gz" -C /workspace/.dotnet
fi
bash scripts/setup.sh
