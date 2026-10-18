<#
.SYNOPSIS
    生成 CommandPark 的发布产物：命令行压缩包 + Inno Setup 安装程序。

.DESCRIPTION
    一次 dotnet publish（或复用已有产物）→ 打包 zip → 用 Inno Setup 生成安装程序。
    压缩包与安装包内容一致：同一份 publish 目录（含 lang\ 翻译、licenses\ 许可证），
    只是「怎么装进系统」不同；两者都排除 *.pdb。

    自启动安装程序需要 Inno Setup 6.5+（6 或 7 都行）：
      自动查找顺序：-Iscc 参数 → 环境变量 INNO_SETUP_ISCC → PATH → Program Files (x86)\Inno Setup 6/7。

.PARAMETER Version
    版本号：写进文件名与 exe 版本信息，必须是数字点分（最多四段），例如 1.2.3。

.PARAMETER Mode
    ReadyToRun（默认）：自包含 + R2R 预编译，启动快，正式发布推荐。
    Aot：Native AOT，启动最快、体积最小，需要 VS 生成工具链。
    FrameworkDependent：不带运行时，体积最小，但用户要自己装 .NET 10 桌面运行时。

.PARAMETER PublishDir
    发布目录；默认 publish\<运行时>-<模式>。配合 -SkipPublish 可以复用已有产物。

.EXAMPLE
    pwsh installer\build.ps1 -Version 1.2.3

.EXAMPLE
    已经 publish 过，只想重打安装包：
    pwsh installer\build.ps1 -Version 1.2.3 -SkipPublish -SkipZip -PublishDir publish\aot
#>
[CmdletBinding()]
param(
    [string]$Version = "0.0.0",
    [ValidateSet("win-x64", "win-arm64")][string]$Runtime = "win-x64",
    [ValidateSet("ReadyToRun", "Aot", "FrameworkDependent")][string]$Mode = "ReadyToRun",
    [string]$Configuration = "Release",
    [string]$PublishDir,
    [string]$OutputDir,
    [string]$Iscc,
    [switch]$SkipPublish,
    [switch]$SkipZip,
    # 要求发布包必须是 Native AOT（CI 发布时用；-Mode Aot 会自动打开）
    [switch]$RequireAot
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = Split-Path $PSScriptRoot -Parent
$uiProject = Join-Path $repoRoot "src\CommandPark.Ui\CommandPark.Ui.csproj"
$issFile = Join-Path $PSScriptRoot "CommandPark.iss"

if (-not $PublishDir) { $PublishDir = Join-Path $repoRoot "publish\$Runtime-$($Mode.ToLowerInvariant())" }
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot "publish\installer" }
$PublishDir = [System.IO.Path]::GetFullPath($PublishDir)
$OutputDir = [System.IO.Path]::GetFullPath($OutputDir)

if ($Version -notmatch '^\d+(\.\d+){0,3}$') {
    throw "版本号必须是数字点分（最多四段），例如 1.2.3；当前是 '$Version'"
}

# ---------------------------------------------------------------------------
# 1) 发布
# ---------------------------------------------------------------------------
if (-not $SkipPublish) {
    Write-Host "==> 发布 $Mode / $Runtime" -ForegroundColor Cyan

    $publishArgs = @(
        "publish", $uiProject,
        "-c", $Configuration,
        "-r", $Runtime,
        "-o", $PublishDir,
        "--nologo",
        "-v", "minimal",
        # 一次只用一个 MSBuild 节点：crossgen2/AOT 已经会吃满 CPU，
        # 再并行编译容易把内存打满（尤其是小内存机器）
        "-m:1"
    )

    switch ($Mode) {
        "ReadyToRun" {
            $publishArgs += @("-p:PublishReadyToRun=true", "--self-contained", "true")
        }
        "Aot" {
            $publishArgs += @(
                "-p:PublishAot=true",
                "-p:SelfContained=true",
                "-p:OptimizationPreference=Speed",
                "-p:StackTraceSupport=false",
                "-p:DebugType=None",
                "-p:DebugSymbols=false"
            )
        }
        "FrameworkDependent" {
            $publishArgs += @("--self-contained", "false")
        }
    }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }
}
elseif (-not (Test-Path (Join-Path $PublishDir "CommandPark.Ui.exe"))) {
    throw "发布目录里没有 CommandPark.Ui.exe：$PublishDir"
}

$published = Get-ChildItem $PublishDir -Recurse -File
Write-Host ("    产物 {0} 个文件，{1:N1} MB" -f $published.Count, (($published | Measure-Object Length -Sum).Sum / 1MB))

# ---------------------------------------------------------------------------
# 2) 产物校验：发布包 = AOT 主程序 + C++ 后台程序 + 数据文件（lang\ / licenses\ / 许可证）
# ---------------------------------------------------------------------------
$required = @(
    "CommandPark.Ui.exe",
    "CommandPark.Background.Cpp.exe",
    "commandpark.json",
    "commandpark.schema.json",
    "lang\zh_CN.json",
    "LICENSE",
    "THIRD-PARTY-NOTICES.md"
)
$missing = $required | Where-Object { -not (Test-Path (Join-Path $PublishDir $_)) }
if ($missing) {
    throw "发布目录缺少必需文件（$PublishDir）：$($missing -join '、')"
}

# 形态判定：AOT 会把托管程序集编进原生 exe，所以没有 CommandPark.Ui.dll
$hasManagedApp = Test-Path (Join-Path $PublishDir "CommandPark.Ui.dll")
$hasCoreLib = Test-Path (Join-Path $PublishDir "System.Private.CoreLib.dll")
$flavor = if (-not $hasManagedApp) { "Native AOT" } elseif ($hasCoreLib) { "自包含（含运行时）" } else { "框架依赖（需要 .NET 运行时）" }
Write-Host ("    形态：{0}" -f $flavor)

if (-not $hasManagedApp -and -not (Test-Path (Join-Path $PublishDir "CommandPark.Ui.runtimeconfig.json"))) {
    Write-Host "    检查：无托管程序集、无运行时配置 → 确认是 Native AOT"
}

if ($RequireAot -or $Mode -eq "Aot") {
    $managedLeftovers = Get-ChildItem $PublishDir -File |
        Where-Object { $_.Name -like "CommandPark*.dll" -or $_.Name -like "CommandPark*.deps.json" -or $_.Name -like "CommandPark*.runtimeconfig.json" }
    if ($managedLeftovers) {
        throw ("要求 AOT 发布，但目录里还有托管产物：{0}`n（AOT 发布只会有一个原生 exe；出现这些通常说明用了非 AOT 的 publish 目录）" -f
            (($managedLeftovers | Select-Object -ExpandProperty Name) -join '、'))
    }
    if (-not $hasManagedApp) { Write-Host "    AOT 校验通过" }
}
# ---------------------------------------------------------------------------
# 2) 压缩包（与安装包内容一致：同一份目录，排除 *.pdb）
# ---------------------------------------------------------------------------
if (-not $SkipZip) {
    $zipPath = Join-Path $OutputDir "CommandPark-$Version-$Runtime.zip"
    New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
    Remove-Item -LiteralPath $zipPath -Force -ErrorAction SilentlyContinue

    Write-Host "==> 打包压缩包 $zipPath" -ForegroundColor Cyan
    Add-Type -AssemblyName System.IO.Compression
    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        $count = 0
        foreach ($file in $published | Where-Object { $_.Extension -ne ".pdb" }) {
            # zip 内部一律用正斜杠（规范要求；反斜杠会让 Linux/macOS 的解压工具把整个路径当成文件名）
            $relative = $file.FullName.Substring($PublishDir.Length + 1).Replace("\", "/")
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, $file.FullName, $relative, [System.IO.Compression.CompressionLevel]::Optimal)
            $count++
        }
    }
    finally {
        $zip.Dispose()
    }
    Write-Host ("    已写入 {0} 个文件，{1:N1} MB" -f $count, ((Get-Item $zipPath).Length / 1MB))
}

# ---------------------------------------------------------------------------
# 3) 安装程序
# ---------------------------------------------------------------------------
function Find-Iscc {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path $Explicit) { return $Explicit }
        throw "指定的 ISCC 不存在：$Explicit"
    }
    if ($env:INNO_SETUP_ISCC -and (Test-Path $env:INNO_SETUP_ISCC)) { return $env:INNO_SETUP_ISCC }

    $fromPath = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($fromPath) { return $fromPath.Source }

    $candidates = @()
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $root) { continue }
        foreach ($edition in @("Inno Setup 7", "Inno Setup 6")) {
            $candidates += (Join-Path $root "$edition\ISCC.exe")
        }
    }
    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) { return $candidate }
    }

    throw "找不到 ISCC.exe（Inno Setup 编译器）。装一个 Inno Setup 6/7，或用 -Iscc 指定路径，或设环境变量 INNO_SETUP_ISCC。"
}

$isccPath = Find-Iscc -Explicit $Iscc
Write-Host "==> 生成安装程序（$isccPath）" -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

& $isccPath "/Qp" "/DMyAppVersion=$Version" "/DPublishDir=$PublishDir" "/DOutputDir=$OutputDir" $issFile
if ($LASTEXITCODE -ne 0) { throw "ISCC 编译失败（退出码 $LASTEXITCODE）" }

$installer = Get-ChildItem $OutputDir -Filter "CommandPark-$Version-setup.exe" | Select-Object -First 1
if (-not $installer) { throw "没有找到生成出来的安装程序：$OutputDir\CommandPark-$Version-setup.exe" }

Write-Host ""
Write-Host "完成：" -ForegroundColor Green
Get-ChildItem $OutputDir | Where-Object { $_.Name -like "CommandPark-$Version-*" } |
    Sort-Object Name | ForEach-Object { Write-Host ("    {0}  ({1:N1} MB)" -f $_.FullName, ($_.Length / 1MB)) }