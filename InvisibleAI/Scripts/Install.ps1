[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[a-p]{32}$')][string[]]$ExtensionId,
    [string]$PackageDirectory = $PSScriptRoot,
    [switch]$RegisterOnly
)
$ErrorActionPreference = 'Stop'
$package = [IO.Path]::GetFullPath($PackageDirectory)
foreach ($exe in @('InvisibleAI.Companion.exe','InvisibleAI.NativeHost.exe')) {
    if (-not (Test-Path -LiteralPath (Join-Path $package $exe))) { throw "Missing $exe. Run Publish.ps1 first." }
}
$destination = if ($RegisterOnly) { $package } else { Join-Path $env:LOCALAPPDATA 'InvisibleAI\app' }
if (-not $RegisterOnly) {
    if (Get-Process -Name 'InvisibleAI.Companion','InvisibleAI.NativeHost' -ErrorAction SilentlyContinue) { throw 'Exit the companion and disconnect the extension before updating.' }
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in Get-ChildItem -LiteralPath $package -File) { Copy-Item -LiteralPath $file.FullName -Destination $destination -Force }
    if (Test-Path -LiteralPath (Join-Path $package 'AI')) { Copy-Item -LiteralPath (Join-Path $package 'AI') -Destination $destination -Recurse -Force }
    # Python venvs are not relocatable. Create the runtime at its final installed path with Setup-Gemini.ps1.
}
$hostManifest = Join-Path $destination 'com.invisibleai.assistant.json'
$manifestObject = @{
    name = 'com.invisibleai.assistant'; description = 'Invisible AI Assistant Windows bridge'
    path = (Join-Path $destination 'InvisibleAI.NativeHost.exe'); type = 'stdio'
    allowed_origins = @($ExtensionId | ForEach-Object { "chrome-extension://$_/" })
}
[IO.File]::WriteAllText($hostManifest, ($manifestObject | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
foreach ($browserKey in @('Software\Google\Chrome\NativeMessagingHosts','Software\Microsoft\Edge\NativeMessagingHosts')) {
    $key = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("$browserKey\com.invisibleai.assistant")
    try { $key.SetValue('', $hostManifest) } finally { $key.Dispose() }
}
if (-not $RegisterOnly) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcutPath = Join-Path ([Environment]::GetFolderPath('Programs')) 'Invisible AI Assistant.lnk'
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = Join-Path $destination 'InvisibleAI.Companion.exe'
    $shortcut.WorkingDirectory = $destination
    $shortcut.Save()
}
Write-Host "Registered Chrome/Edge native host. Start $(Join-Path $destination 'InvisibleAI.Companion.exe'), then reconnect the extension."
