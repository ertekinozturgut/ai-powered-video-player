param(
    [string]$Python = "python",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot
$venv = Join-Path $projectRoot ".venv-mcp"
$pythonExecutable = Join-Path $venv "Scripts\python.exe"
$publishDirectory = Join-Path $projectRoot "artifacts\mcp"

& $Python -m venv $venv
& $pythonExecutable -m pip install --upgrade pip
& $pythonExecutable -m pip install -r (Join-Path $projectRoot "requirements-local-whisper.txt")
dotnet publish (Join-Path $projectRoot "src\VideoAtlas.Mcp\VideoAtlas.Mcp.csproj") -c $Configuration -o $publishDirectory

Write-Host "VideoAtlas MCP is ready in $publishDirectory"
Write-Host "Python executable: $pythonExecutable"
