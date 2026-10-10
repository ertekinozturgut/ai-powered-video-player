#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
python_command="${PYTHON_COMMAND:-python3}"
venv="$project_root/.venv-mcp"
publish_directory="$project_root/artifacts/mcp"

"$python_command" -m venv "$venv"
"$venv/bin/python" -m pip install --upgrade pip
"$venv/bin/python" -m pip install -r "$project_root/requirements-local-whisper.txt"
dotnet publish "$project_root/src/VideoAtlas.Mcp/VideoAtlas.Mcp.csproj" -c Release -o "$publish_directory"

printf 'VideoAtlas MCP is ready in %s\n' "$publish_directory"
