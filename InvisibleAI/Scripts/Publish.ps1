[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [string]$Python = 'python')
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$out = Join-Path $projectRoot 'artifacts\helper'
function Checked([scriptblock]$Command) { & $Command; if ($LASTEXITCODE -ne 0) { throw "Publish command failed: $LASTEXITCODE" } }
Checked { & $Dotnet publish (Join-Path $projectRoot 'LocalHelper\InvisibleAI.Helper.csproj') -c Release -r win-x64 --self-contained true -o $out }
Checked { & $Python -m PyInstaller --noconfirm --clean --onedir --name GeminiWorker --distpath $out --workpath (Join-Path $projectRoot 'artifacts\pyinstaller-work') --specpath (Join-Path $projectRoot 'artifacts') --collect-all gemini_webapi (Join-Path $projectRoot 'LocalHelper\AI\GeminiWeb\worker.py') }
$zip = Join-Path $projectRoot 'artifacts\helper.zip'
if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Checked { & $Dotnet publish (Join-Path $projectRoot 'Installer\InvisibleAI.Setup.csproj') -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o (Join-Path $projectRoot 'artifacts\browser-first-release') }
Push-Location (Join-Path $projectRoot 'BrowserExtension')
try { Checked { npm run build } } finally { Pop-Location }
$extensionOut = Join-Path $projectRoot 'artifacts\browser-first-release\BrowserExtension'
New-Item -ItemType Directory -Path $extensionOut -Force | Out-Null
Copy-Item -Path (Join-Path $projectRoot 'BrowserExtension\dist\*') -Destination $extensionOut -Recurse -Force
Write-Host "Ready: artifacts\browser-first-release\InvisibleAI.Setup.exe and BrowserExtension"
