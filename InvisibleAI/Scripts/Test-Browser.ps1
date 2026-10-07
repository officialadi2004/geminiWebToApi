[CmdletBinding()]
param([string]$Dotnet = 'dotnet', [ValidateSet('Chrome','Edge')][string]$Browser = 'Edge', [switch]$UpgradeOnly)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$qa = Join-Path $projectRoot 'artifacts\qa'
New-Item -ItemType Directory -Path $qa -Force | Out-Null
& $Dotnet publish (Join-Path $projectRoot 'Tests\InvisibleAI.Tests.csproj') -c Release -r win-x64 --self-contained true -o (Join-Path $qa 'testhost')
if ($LASTEXITCODE -ne 0) { throw 'Test helper publish failed.' }
$identity = Get-Content (Join-Path $projectRoot 'Shared\Protocol\ExtensionIdentity.cs') -Raw
$id = [regex]::Match($identity, 'Id = "([a-p]+)"').Groups[1].Value
$manifest = Join-Path $qa 'test-host.json'
@{name='com.invisibleai.assistant';description='Synthetic test helper';path=(Join-Path $qa 'testhost\InvisibleAI.Tests.exe');type='stdio';allowed_origins=@("chrome-extension://$id/")} | ConvertTo-Json | Set-Content -LiteralPath $manifest
$keys = @('Software\Google\Chrome\NativeMessagingHosts\com.invisibleai.assistant','Software\Microsoft\Edge\NativeMessagingHosts\com.invisibleai.assistant')
$backup = @()
try {
    foreach ($name in $keys) {
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($name)
        $backup += @{name=$name; exists=($null -ne $key); value=if($key){$key.GetValue('')}else{$null}}
        if($key){$key.Dispose()}
        $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($name); $key.SetValue('', $manifest); $key.Dispose()
    }
    Push-Location (Join-Path $projectRoot 'BrowserExtension')
    try {
        npm run build
        if ($LASTEXITCODE -ne 0) { throw 'Extension build failed.' }
        $browserArguments = @(); if ($Browser -eq 'Edge') { $browserArguments += '--edge' }; if ($UpgradeOnly) { $browserArguments += '--upgrade-only' }
        node scripts/browser-smoke.mjs @browserArguments
        if ($LASTEXITCODE -ne 0) { throw 'Browser smoke failed.' }
    } finally { Pop-Location }
} finally {
    foreach($entry in $backup) {
        if($entry.exists){$key=[Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($entry.name);$key.SetValue('', $entry.value);$key.Dispose()}
        else{[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($entry.name,$false)}
    }
}
