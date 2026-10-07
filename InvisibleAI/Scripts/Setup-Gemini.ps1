[CmdletBinding()]
param([string]$Python = 'python', [string]$PackageDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
if (-not $PackageDirectory) {
    $PackageDirectory = if (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'InvisibleAI.Companion.exe')) { $PSScriptRoot } else { Join-Path $projectRoot 'artifacts\win-x64' }
}
$package = [IO.Path]::GetFullPath($PackageDirectory)
$requirements = Join-Path $package 'AI\GeminiWeb\requirements.txt'
if (-not (Test-Path -LiteralPath $requirements)) { throw 'Publish the companion before setting up Gemini.' }
& $Python -m venv (Join-Path $package 'gemini-runtime')
if ($LASTEXITCODE -ne 0) { throw 'Install Python 3.11+ and try again.' }
& (Join-Path $package 'gemini-runtime\Scripts\python.exe') -m pip install -r $requirements
if ($LASTEXITCODE -ne 0) { throw 'Gemini dependency installation failed.' }
Write-Host 'Gemini runtime ready. No credentials are written by this script.'
