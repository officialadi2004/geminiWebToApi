[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
# Unregister only our keys. Preserve settings, credentials and files for deliberate user removal.
foreach ($browserKey in @('Software\Google\Chrome\NativeMessagingHosts','Software\Microsoft\Edge\NativeMessagingHosts')) {
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree("$browserKey\com.invisibleai.assistant", $false)
}
$run = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Software\Microsoft\Windows\CurrentVersion\Run', $true)
if ($run) { try { $run.DeleteValue('InvisibleAI', $false) } finally { $run.Dispose() } }
$shortcut = Join-Path ([Environment]::GetFolderPath('Programs')) 'Invisible AI Assistant.lnk'
if (Test-Path -LiteralPath $shortcut) { Remove-Item -LiteralPath $shortcut }
Write-Host 'Browser bridge, startup entry, and shortcut removed. Exit the companion, remove its app folder, and remove InvisibleAI/OpenAI from Windows Credential Manager if desired.'
