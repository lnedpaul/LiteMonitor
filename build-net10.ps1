# LiteMonitor .NET 10 单文件发布脚本
# 顺序不可颠倒：先发布独立更新器 -> 覆盖 resources -> 再发布主程序（把更新器嵌进 exe）。
# 否则 exe 里会带上旧版（.NET 8）更新器，自动更新链路静默失效。
param(
    [string] $DotNet = "F:\AI agents\DSH\CSII mod\_sdk10\dotnet\dotnet.exe",
    [string] $Configuration = "Release",
    [string] $OutDir = "dist\LiteMonitor-single"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    if (-not (Test-Path $DotNet)) {
        Write-Host "Portable SDK not found, falling back to dotnet in PATH"
        $DotNet = "dotnet"
    }

    # ---- 1. 发布独立更新器（自包含单文件）----
    & $DotNet publish "LiteMonitor.Updater\LiteMonitor.Updater.csproj" -c $Configuration -r win-x64 -o "$OutDir\_updater" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed (exit $LASTEXITCODE)" }

    $fresh = Join-Path $root "$OutDir\_updater\LiteMonitor.Updater.exe"
    if (-not (Test-Path $fresh)) { throw "Updater exe not found: $fresh" }
    Copy-Item $fresh "resources\LiteMonitor.Updater.exe" -Force

    # ---- 2. 发布主程序：自包含 + 单文件 + 资源内嵌 ----
    # PublishSingleFile 同时是 csproj 里「资源改为 EmbeddedResource」的开关。
    & $DotNet publish "LiteMonitor.csproj" -c $Configuration -r win-x64 --self-contained true `
        -p:PublishSingleFile=true `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -p:EnableCompressionInSingleFile=true `
        -p:PublishTrimmed=false `
        -p:DebugType=none -p:DebugSymbols=false `
        -o "$OutDir\_app" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "App publish failed (exit $LASTEXITCODE)" }

    $exe = Join-Path $root "$OutDir\_app\LiteMonitor.exe"
    if (-not (Test-Path $exe)) { throw "LiteMonitor.exe not found at $exe" }

    # ---- 3. 自检 ----
    # a) 资源必须嵌入 exe，而不是作为外部内容复制出来
    if (Test-Path "$OutDir\_app\resources") {
        throw "Resources were copied as external content instead of embedded into the exe"
    }
    # b) 单文件输出不应散落托管 / native DLL
    $extras = @(Get-ChildItem "$OutDir\_app" -Force | Where-Object { $_.Name -ne "LiteMonitor.exe" })
    if ($extras.Count -gt 0) {
        Write-Host ("WARN: extra items in publish output: " + ($extras.Name -join ", "))
    }
    # c) 体积下限：73 MB 的更新器已嵌入，exe 不该只有运行时大小
    if ((Get-Item $exe).Length -lt 70MB) {
        throw "Executable is unexpectedly small - embedded updater may be missing"
    }

    $updaterHash = (Get-FileHash $fresh -Algorithm SHA256).Hash

    # ---- 4. 只保留最终 exe，清掉中间目录 ----
    Copy-Item $exe (Join-Path $root "$OutDir\LiteMonitor.exe") -Force
    Remove-Item "$OutDir\_app" -Recurse -Force
    Remove-Item "$OutDir\_updater" -Recurse -Force

    $final = Join-Path $root "$OutDir\LiteMonitor.exe"
    $sizeMB = [math]::Round((Get-Item $final).Length / 1MB, 2)
    $hash = (Get-FileHash $final -Algorithm SHA256).Hash

    Write-Host "OK: $final ($sizeMB MB)"
    Write-Host "OK: single-file output contains only LiteMonitor.exe"
    Write-Host "OK: embedded updater SHA256 = $updaterHash"
    Write-Host "OK: package SHA256 = $hash"
}
finally {
    Pop-Location
}
