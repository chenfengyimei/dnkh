# 电脑开荒发布脚本：产出自包含单文件 exe（目标机免装 .NET 运行时）
# Publish script: builds and verifies a self-contained single-file executable.
param(
    [string]$Output = "publish"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$outputFull = [System.IO.Path]::GetFullPath($Output)
$target = Join-Path $outputFull "Dnkhw.exe"
$staging = Join-Path ([System.IO.Path]::GetTempPath()) ("dnkhw-publish-" + [guid]::NewGuid().ToString("N"))
$incoming = $target + ".new-" + [guid]::NewGuid().ToString("N")
$backup = $target + ".backup-" + [guid]::NewGuid().ToString("N")

# 旧脚本未内嵌的 WPF 原生伴随库；新版成功替换后只清理这些明确的生成文件。
$legacyCompanions = @(
    "D3DCompiler_47_cor3.dll",
    "PenImc_cor3.dll",
    "PresentationNative_cor3.dll",
    "vcruntime140_cor3.dll",
    "wpfgfx_cor3.dll"
)

New-Item -ItemType Directory -Path $staging -Force | Out-Null
try {
    # 先发布到独立临时目录；构建或校验失败时保留当前可用版本。
    dotnet publish src/Dnkhw/Dnkhw.csproj -c Release -r win-x64 --self-contained true `
        -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
        -p:IncludeNativeLibrariesForSelfExtract=true `
        -o $staging

    if ($LASTEXITCODE -ne 0) {
        throw "publish failed (exit $LASTEXITCODE)"
    }

    $stagedTarget = Join-Path $staging "Dnkhw.exe"
    $stagedEntries = @(Get-ChildItem -LiteralPath $staging -Force)
    if (-not (Test-Path -LiteralPath $stagedTarget) -or $stagedEntries.Count -ne 1) {
        $names = ($stagedEntries | Select-Object -ExpandProperty Name) -join ", "
        throw "publish staging is not a single executable; entries: $names"
    }

    New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
    Copy-Item -LiteralPath $stagedTarget -Destination $incoming

    # 同卷临时文件原子替换：目标被运行中程序/杀软锁定时旧版本不会先被删除。
    if (Test-Path -LiteralPath $target) {
        [System.IO.File]::Replace($incoming, $target, $backup)
        Remove-Item -LiteralPath $backup -Force
    } else {
        Move-Item -LiteralPath $incoming -Destination $target
    }

    foreach ($name in $legacyCompanions) {
        $legacyPath = Join-Path $outputFull $name
        if (Test-Path -LiteralPath $legacyPath) {
            Remove-Item -LiteralPath $legacyPath -Force
        }
    }

    $unexpected = @(Get-ChildItem -LiteralPath $outputFull -Force | Where-Object {
        $_.FullName -ne (Get-Item -LiteralPath $target).FullName
    })
    if ($unexpected.Count -gt 0) {
        $names = ($unexpected | Select-Object -ExpandProperty Name) -join ", "
        throw "publish output contains unexpected files: $names"
    }

    Write-Host "Published: $target"
} finally {
    if (Test-Path -LiteralPath $incoming) {
        Remove-Item -LiteralPath $incoming -Force -ErrorAction SilentlyContinue
    }
    if ((Test-Path -LiteralPath $backup) -and (Test-Path -LiteralPath $target)) {
        Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path -LiteralPath $staging) {
        [System.IO.Directory]::Delete($staging, $true)
    }
}
