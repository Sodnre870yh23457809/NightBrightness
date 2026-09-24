$ErrorActionPreference = 'Stop'
$dataPath = Join-Path $env:LOCALAPPDATA 'NightBrightness'
$appPath = Join-Path $dataPath 'app\NightBrightness.exe'
$running = @(Get-Process NightBrightness -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $appPath })
if ($running.Count) {
    Set-Content -LiteralPath (Join-Path $dataPath 'exit.request') -Value 'restore and exit'
    foreach ($process in $running) {
        if (-not $process.WaitForExit(15000)) { throw 'App did not exit. Use its tray menu to restore 55% and exit, then retry.' }
    }
} elseif (Test-Path -LiteralPath $appPath) {
    $check = Start-Process -FilePath $appPath -ArgumentList '--verify-driver' -WindowStyle Hidden -Wait -PassThru
    if ($check.ExitCode -ne 0) { throw 'Brightness restore failed. See activity.log.' }
}
Remove-ItemProperty -LiteralPath 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'NightBrightness' -ErrorAction SilentlyContinue
$taskService = New-Object -ComObject 'Schedule.Service'
$taskService.Connect()
try { $taskService.GetFolder('\').DeleteTask('NightBrightness', 0) }
catch { if ($_.Exception.HResult -ne -2147024894) { throw } }
$desktopPath = [Environment]::GetFolderPath('Desktop')
foreach ($name in @('Night Brightness.lnk', 'Restore Brightness 55%.lnk', 'Restore Day Brightness.lnk')) {
    $linkPath = Join-Path $desktopPath $name
    if (Test-Path -LiteralPath $linkPath) { Remove-Item -LiteralPath $linkPath }
}
Write-Output 'Night Brightness stopped, 55% restored, startup and shortcuts removed. Application files and backups retained.'
