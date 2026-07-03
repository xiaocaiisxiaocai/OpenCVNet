param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0-local"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

& "$PSScriptRoot\ci-local.ps1" -Configuration $Configuration -Version $Version

$packageDir = Join-Path $root "artifacts\package"
$zip = Join-Path $root "artifacts\VisionInspection-$Version.zip"
if (Test-Path $packageDir) { Remove-Item $packageDir -Recurse -Force }
New-Item -ItemType Directory -Force $packageDir | Out-Null

Copy-Item artifacts\app $packageDir\app -Recurse
Copy-Item artifacts\watchdog $packageDir\watchdog -Recurse
Copy-Item artifacts\plc-probe $packageDir\plc-probe -Recurse

$manifest = Join-Path $packageDir "release-manifest.txt"
@(
    "Name=VisionInspection"
    "Version=$Version"
    "BuiltAtUtc=$((Get-Date).ToUniversalTime().ToString('O'))"
    "App=app\VisionInspection.App.exe"
    "Watchdog=watchdog\VisionInspection.Watchdog.exe"
    "PlcProbe=plc-probe\VisionInspection.PlcProbe.exe"
) | Set-Content -Encoding UTF8 $manifest

Get-ChildItem $packageDir -Recurse -File |
    Where-Object { $_.FullName -ne $manifest } |
    Sort-Object FullName |
    ForEach-Object {
        $hash = Get-FileHash $_.FullName -Algorithm SHA256
        "$($hash.Hash)  $($_.FullName.Substring($packageDir.Length + 1))"
    } | Add-Content -Encoding UTF8 $manifest

if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $packageDir "*") -DestinationPath $zip

Write-Host "发布包已生成：$zip"
