# 注册开机自启（当前用户登录后自动启动看门狗，由看门狗拉起主程序）
# 用法：右键“使用 PowerShell 运行”，或： powershell -ExecutionPolicy Bypass -File install-autostart.ps1

param(
    [string]$WatchdogExe = (Join-Path $PSScriptRoot 'VisionInspection.Watchdog.exe')
)

if (-not (Test-Path $WatchdogExe)) {
    Write-Error "未找到看门狗程序：$WatchdogExe（请将脚本与程序放在同一目录，或用 -WatchdogExe 指定路径）"
    exit 1
}

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Set-ItemProperty -Path $runKey -Name 'VisionInspectionWatchdog' -Value "`"$WatchdogExe`""

Write-Host "已注册开机自启：" -NoNewline
Write-Host $WatchdogExe -ForegroundColor Green
Write-Host ""
Write-Host "提示：无人值守现场还需配置【自动登录】，使系统开机后自动进入桌面会话（见 docs/deployment.md）。"
