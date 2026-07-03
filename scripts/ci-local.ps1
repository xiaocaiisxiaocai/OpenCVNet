param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0-local"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location $root

function Invoke-CiStep {
    param([scriptblock]$Command)
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "命令失败，退出码：$LASTEXITCODE"
    }
}

Invoke-CiStep { dotnet restore tests\VisionInspection.Tests\VisionInspection.Tests.csproj }
Invoke-CiStep { dotnet build tests\VisionInspection.Tests\VisionInspection.Tests.csproj -c $Configuration --no-restore /p:Version=$Version /p:InformationalVersion="$Version+local" }
Invoke-CiStep { dotnet test tests\VisionInspection.Tests\VisionInspection.Tests.csproj -c $Configuration --no-build --verbosity normal --logger "trx;LogFileName=test-results.trx" --results-directory TestResults }

Invoke-CiStep { dotnet publish src\VisionInspection.App\VisionInspection.App.csproj -c $Configuration --no-restore -o artifacts\app /p:Version=$Version /p:InformationalVersion="$Version+local" }
Invoke-CiStep { dotnet publish src\VisionInspection.Watchdog\VisionInspection.Watchdog.csproj -c $Configuration --no-restore -o artifacts\watchdog /p:Version=$Version /p:InformationalVersion="$Version+local" }
Invoke-CiStep { dotnet publish src\VisionInspection.PlcProbe\VisionInspection.PlcProbe.csproj -c $Configuration --no-restore -o artifacts\plc-probe /p:Version=$Version /p:InformationalVersion="$Version+local" }

$required = @(
    "artifacts\app\VisionInspection.App.exe",
    "artifacts\app\OpenCvSharpExtern.dll",
    "artifacts\watchdog\VisionInspection.Watchdog.exe",
    "artifacts\plc-probe\VisionInspection.PlcProbe.exe"
)

foreach ($file in $required) {
    if (!(Test-Path $file)) { throw "缺少发布产物：$file" }
    if ((Get-Item $file).Length -le 0) { throw "发布产物为空：$file" }
}

$appVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo("artifacts\app\VisionInspection.App.exe").ProductVersion
if ([string]::IsNullOrWhiteSpace($appVersion)) {
    throw "VisionInspection.App.exe 缺少 ProductVersion"
}

Write-Host "本地 CI 通过，App 版本：$appVersion"
