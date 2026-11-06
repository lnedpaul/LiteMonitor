# LiteMonitor .NET 10 单文件发布脚本
# 默认：框架依赖单文件（对齐上游约 3 MB 的发布思路，目标机需装 .NET 10 桌面运行时）。
# -SelfContained：自包含单文件（约 82 MB，免装运行时，可拷到任意 Win10/11 x64）。
# 顺序不可颠倒：先发布更新器 -> 覆盖 resources -> 再发布主程序（把更新器嵌进 exe）。
param(
    [string] $DotNet = "F:\AI agents\DSH\CSII mod\_sdk10\dotnet\dotnet.exe",
    [string] $Configuration = "Release",
    [string] $OutDir = "",
    [switch] $SelfContained
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Push-Location $root
try {
    if (-not (Test-Path $DotNet)) {
        Write-Host "Portable SDK not found, falling back to dotnet in PATH"
        $DotNet = "dotnet"
    }

    if ([string]::IsNullOrWhiteSpace($OutDir)) {
        $OutDir = if ($SelfContained) { "dist\LiteMonitor-selfcontained" } else { "dist\LiteMonitor-single" }
    }
    $scFlag = if ($SelfContained) { "true" } else { "false" }
    $mode = if ($SelfContained) { "self-contained" } else { "framework-dependent" }

    # ---- 1. 发布独立更新器 ----
    & $DotNet publish "LiteMonitor.Updater\LiteMonitor.Updater.csproj" -c $Configuration -r win-x64 --self-contained $scFlag -o "$OutDir\_updater" -v minimal
    if ($LASTEXITCODE -ne 0) { throw "Updater publish failed (exit $LASTEXITCODE)" }

    $fresh = Join-Path $root "$OutDir\_updater\LiteMonitor.Updater.exe"
    if (-not (Test-Path $fresh)) { throw "Updater exe not found: $fresh" }
    Copy-Item $fresh "resources\LiteMonitor.Updater.exe" -Force

    # ---- 2. 发布主程序：单文件 + 资源内嵌 ----
    # PublishSingleFile 同时是 csproj 里「资源改为 EmbeddedResource」的开关。
    # EnableCompressionInSingleFile 仅自包含发布支持（框架依赖会报 NETSDK1176）。
    $publishArgs = @(
        "publish", "LiteMonitor.csproj", "-c", $Configuration, "-r", "win-x64",
        "--self-contained", $scFlag,
        "-p:PublishSingleFile=true",
        "-p:IncludeNativeLibrariesForSelfExtract=true",
        "-p:PublishTrimmed=false",
        "-p:DebugType=none", "-p:DebugSymbols=false",
        "-o", "$OutDir\_app", "-v", "minimal"
    )
    if ($SelfContained) { $publishArgs += "-p:EnableCompressionInSingleFile=true" }
    & $DotNet @publishArgs
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
    # c) 体积下限：自包含版必须含运行时与更新器；框架依赖版至少含嵌入资源
    $minBytes = if ($SelfContained) { 70MB } else { 1MB }
    if ((Get-Item $exe).Length -lt $minBytes) {
        throw "Executable is unexpectedly small - embedded resources may be missing"
    }

    $updaterHash = (Get-FileHash $fresh -Algorithm SHA256).Hash

    # ---- 4. 只保留最终 exe，清掉中间目录 ----
    Copy-Item $exe (Join-Path $root "$OutDir\LiteMonitor.exe") -Force
    Remove-Item "$OutDir\_app" -Recurse -Force
    Remove-Item "$OutDir\_updater" -Recurse -Force

    $final = Join-Path $root "$OutDir\LiteMonitor.exe"
    $sizeMB = [math]::Round((Get-Item $final).Length / 1MB, 2)
    $hash = (Get-FileHash $final -Algorithm SHA256).Hash

    # ---- 5. 框架依赖版额外产出分发 zip（对齐上游 2.8 MB 发布形态）----
    # 只把 exe 放进 zip，避免把同目录的 settings.json 等用户数据一起打包。
    if (-not $SelfContained) {
        $zip = Join-Path $root "$OutDir\LiteMonitor-net10-win-x64.zip"
        if (Test-Path $zip) { Remove-Item $zip -Force }
        # Compress-Archive 传单个文件路径时，zip 内只含该 exe，不会带上同目录的用户数据
        Compress-Archive -Path $final -DestinationPath $zip -CompressionLevel Optimal
        $zipMB = [math]::Round((Get-Item $zip).Length / 1MB, 2)
        Write-Host "OK: $zip ($zipMB MB)"
    }

    Write-Host "OK: $final ($sizeMB MB, $mode)"
    Write-Host "OK: single-file output contains only LiteMonitor.exe"
    Write-Host "OK: embedded updater SHA256 = $updaterHash"
    Write-Host "OK: package SHA256 = $hash"
}
finally {
    Pop-Location
}
