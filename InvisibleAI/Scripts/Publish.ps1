[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [ValidateSet('win-x64','win-arm64')][string]$Runtime = 'win-x64', [switch]$SelfContained)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$out = Join-Path $projectRoot "artifacts\$Runtime"
# Both exes share the same runtime/configuration. Do not single-file bundle the native host.
& $Dotnet publish (Join-Path $projectRoot 'WindowsCompanion\InvisibleAI.Companion.csproj') -c Release -r $Runtime --self-contained $SelfContained.IsPresent -o $out
if ($LASTEXITCODE -ne 0) { throw 'Companion publish failed.' }
& $Dotnet publish (Join-Path $projectRoot 'WindowsCompanion\NativeMessaging\Host\InvisibleAI.NativeHost.csproj') -c Release -r $Runtime --self-contained $SelfContained.IsPresent -o $out
if ($LASTEXITCODE -ne 0) { throw 'Native host publish failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Destination $out
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Uninstall.ps1') -Destination $out
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Setup-Gemini.ps1') -Destination $out
Write-Host "Published: $out"
