param(
    [string]$AllowedRoots = (Get-Location).Path
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:VIDEOATLAS_MCP_ALLOWED_ROOTS = $AllowedRoots
$env:PYTHON_EXECUTABLE = Join-Path $projectRoot ".venv-mcp\Scripts\python.exe"
$server = Join-Path $projectRoot "artifacts\mcp\VideoAtlas.Mcp.dll"

if (-not (Test-Path -LiteralPath $server)) {
    throw "MCP publish output is missing. Run scripts/setup-mcp.ps1 first."
}

dotnet $server
