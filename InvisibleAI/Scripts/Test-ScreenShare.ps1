[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [ValidateSet('Chrome','Edge')][string]$Browser = 'Edge')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$hostPath = Join-Path $projectRoot 'artifacts\qa\capturehost'
& $Dotnet publish (Join-Path $projectRoot 'Tests\InvisibleAI.Tests.csproj') -c Release -r win-x64 --self-contained true -o $hostPath
if ($LASTEXITCODE -ne 0) { throw 'Synthetic capture fixture build failed.' }
Push-Location (Join-Path $projectRoot 'BrowserExtension')
try {
    node scripts/browser-share-smoke.mjs $Browser (Join-Path $hostPath 'InvisibleAI.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Browser screen-share capture test failed.' }
} finally { Pop-Location }
