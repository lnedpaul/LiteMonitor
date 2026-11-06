# LiteMonitor .NET 10 发布脚本
# 顺序不可颠倒：先发布独立更新器 -> 覆盖 resources -> 再发布主程序。
# 否则主程序会带上仓库里预编译的旧版（.NET 8）更新器，导致自动更新链路静默失效。
param(
    [string] $DotNet = "F:\AI agents\DSH\CSII mod\_sdk10\dotnet\dotnet.exe",
    [string] $Configuration = "Release",
    [string] $OutDir = "dist\publish"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    if (-not (Test-Path $DotNet)) {
        Write-Host "便携 SDK 未找到，改用 PATH 中的 dotnet"
        $DotNet = "dotnet"
    }

    & $DotNet publish "LiteMonitor.Updater\LiteMonitor.Updater.csproj" -c $Configuration -r win-x64 -o "$OutDir\updater" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed (exit $LASTEXITCODE)" }

    Copy-Item "$OutDir\updater\LiteMonitor.Updater.exe" "resources\LiteMonitor.Updater.exe" -Force

    # 主程序保持框架依赖发布（与上游一致），需目标机器装有 .NET 10 Desktop Runtime
    & $DotNet publish "LiteMonitor.csproj" -c $Configuration -o "$OutDir\app" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "App publish failed (exit $LASTEXITCODE)" }

    $exe = Join-Path $root "$OutDir\app\LiteMonitor.exe"
    if (-not (Test-Path $exe)) { throw "LiteMonitor.exe not found at $exe" }

    # 自检：最终发布包里的更新器必须与刚重新发布的一致，
    # 否则说明 publish 漏复制或复制了旧二进制（"新主程序 + 旧更新器"混合包）。
    $fresh = Join-Path $root "$OutDir\updater\LiteMonitor.Updater.exe"
    $shipped = Join-Path $root "$OutDir\app\resources\LiteMonitor.Updater.exe"
    if (-not (Test-Path $shipped)) { throw "Updater missing from publish output: $shipped" }
    $h1 = (Get-FileHash $fresh -Algorithm SHA256).Hash
    $h2 = (Get-FileHash $shipped -Algorithm SHA256).Hash
    if ($h1 -ne $h2) { throw "Updater hash mismatch - publish output contains a stale updater" }

    Write-Host "OK: $exe"
    Write-Host "OK: updater verified ($h1)"
}
finally {
    Pop-Location
}
