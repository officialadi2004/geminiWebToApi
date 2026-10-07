[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Python = 'python', [switch]$SkipExtension, [switch]$UiTests)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
function Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Build command failed: $LASTEXITCODE" } }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:INVISIBLEAI_GEMINI_PYTHON = $Python
Checked { & $Python -m ruff check --config (Join-Path $projectRoot 'ruff.toml') (Join-Path $projectRoot 'WindowsCompanion\AI\GeminiWeb') (Join-Path $projectRoot 'Tests\test_gemini_worker.py') }
Checked { & $Python -m unittest discover -s (Join-Path $projectRoot 'Tests') -p 'test_*.py' -v }
Checked { & $Dotnet build (Join-Path $projectRoot 'InvisibleAI.slnx') -c Release }
if ($UiTests) { Checked { & $Dotnet run --project (Join-Path $projectRoot 'Tests\InvisibleAI.Tests.csproj') -c Release --no-build -- --ui } }
else { Checked { & $Dotnet run --project (Join-Path $projectRoot 'Tests\InvisibleAI.Tests.csproj') -c Release --no-build } }
if (-not $SkipExtension) {
    Push-Location (Join-Path $projectRoot 'BrowserExtension')
    try { Checked { npm ci --no-audit --no-fund }; Checked { npm run lint }; Checked { npm run check }; Checked { npm test } } finally { Pop-Location }
}
