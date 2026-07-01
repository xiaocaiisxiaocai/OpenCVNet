# 取消开机自启
# 用法： powershell -ExecutionPolicy Bypass -File uninstall-autostart.ps1

$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
if (Get-ItemProperty -Path $runKey -Name 'VisionInspectionWatchdog' -ErrorAction SilentlyContinue) {
    Remove-ItemProperty -Path $runKey -Name 'VisionInspectionWatchdog'
    Write-Host "已取消开机自启。" -ForegroundColor Green
} else {
    Write-Host "未发现自启项，无需处理。"
}
