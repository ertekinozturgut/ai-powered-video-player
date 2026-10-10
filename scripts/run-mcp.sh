#!/usr/bin/env bash
set -euo pipefail

project_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
export VIDEOATLAS_MCP_ALLOWED_ROOTS="${VIDEOATLAS_MCP_ALLOWED_ROOTS:-$PWD}"
export PYTHON_EXECUTABLE="${PYTHON_EXECUTABLE:-$project_root/.venv-mcp/bin/python}"
server="$project_root/artifacts/mcp/VideoAtlas.Mcp.dll"

if [[ ! -f "$server" ]]; then
  printf 'MCP publish output is missing. Run scripts/setup-mcp.sh first.\n' >&2
  exit 1
fi

exec dotnet "$server"
