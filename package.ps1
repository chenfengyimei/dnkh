# 电脑开荒发行打包：同一份已验证的单文件程序，生成安装版、免安装 ZIP 与在线更新用 EXE。
param(
    [string]$Output = "release"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = $PSScriptRoot
$project = Join-Path $root "src\Dnkhw\Dnkhw.csproj"
$packageScript = Join-Path $root "publish.ps1"
$installerScript = Join-Path $root "installer\Dnkhw.iss"
$portableReadme = Join-Path $root "installer\免安装版使用说明.txt"
$outputFull = [System.IO.Path]::GetFullPath((Join-Path $root $Output))
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("dnkhw-package-" + [Guid]::NewGuid().ToString("N"))

function Find-Iscc {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($command) { return $command.Source }

    $candidates = @(
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe"),
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    return $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}

[xml]$projectXml = Get-Content -LiteralPath $project -Raw
$version = ([string]$projectXml.Project.PropertyGroup.Version).Trim()
if ([string]::IsNullOrWhiteSpace($version) -or $version -notmatch '^\d+(\.\d+){1,3}$') {
    throw "Invalid project version: '$version'"
}

$iscc = Find-Iscc
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it first (winget install --id JRSoftware.InnoSetup --exact)."
}

$publishDir = Join-Path $tempRoot "publish"
$portableDir = Join-Path $tempRoot "portable"
$artifactDir = Join-Path $tempRoot "artifacts"
$portableZipName = "Dnkhw-v$version-Portable.zip"
$setupName = "Dnkhw-v$version-Setup.exe"
$updateName = "Dnkhw.exe"

New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null
try {
    & $packageScript -Output $publishDir
    if ($LASTEXITCODE -ne 0) { throw "publish failed (exit $LASTEXITCODE)" }

    $publishedExe = Join-Path $publishDir $updateName
    if (-not (Test-Path -LiteralPath $publishedExe)) {
        throw "Published executable was not found: $publishedExe"
    }

    New-Item -ItemType Directory -Path $portableDir -Force | Out-Null
    Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $portableDir $updateName)
    Copy-Item -LiteralPath $portableReadme -Destination (Join-Path $portableDir (Split-Path $portableReadme -Leaf))
    Compress-Archive -Path (Join-Path $portableDir '*') -DestinationPath (Join-Path $artifactDir $portableZipName) -CompressionLevel Optimal

    & $iscc "/DMyAppVersion=$version" "/DMyAppSource=$publishDir" "/DMyAppOutput=$artifactDir" "/DMyAppBaseName=$([IO.Path]::GetFileNameWithoutExtension($setupName))" $installerScript
    if ($LASTEXITCODE -ne 0) { throw "Inno Setup compilation failed (exit $LASTEXITCODE)" }

    Copy-Item -LiteralPath $publishedExe -Destination (Join-Path $artifactDir $updateName)

    $expected = @($updateName, $setupName, $portableZipName) | Sort-Object
    $actual = @(Get-ChildItem -LiteralPath $artifactDir -File | Select-Object -ExpandProperty Name | Sort-Object)
    if (Compare-Object -ReferenceObject $expected -DifferenceObject $actual) {
        throw "Unexpected packaging output: $($actual -join ', ')"
    }

    New-Item -ItemType Directory -Path $outputFull -Force | Out-Null
    foreach ($name in $expected) {
        $stalePrefix = ".$name.new-"
        Get-ChildItem -LiteralPath $outputFull -File | Where-Object {
            $_.Name.StartsWith($stalePrefix, [StringComparison]::Ordinal)
        } | ForEach-Object {
            Remove-Item -LiteralPath $_.FullName -Force
        }
    }

    foreach ($name in $expected) {
        $incoming = Join-Path $outputFull (".$name.new-" + [Guid]::NewGuid().ToString("N"))
        $destination = Join-Path $outputFull $name
        $backup = Join-Path $outputFull (".$name.backup-" + [Guid]::NewGuid().ToString("N"))
        try {
            Copy-Item -LiteralPath (Join-Path $artifactDir $name) -Destination $incoming
            if (Test-Path -LiteralPath $destination) {
                [System.IO.File]::Replace($incoming, $destination, $backup)
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            } else {
                Move-Item -LiteralPath $incoming -Destination $destination
            }
        } finally {
            Remove-Item -LiteralPath $incoming -Force -ErrorAction SilentlyContinue
            if ((Test-Path -LiteralPath $backup) -and (Test-Path -LiteralPath $destination)) {
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Write-Host "Packaged v${version}:"
    Get-ChildItem -LiteralPath $outputFull -File | Where-Object { $_.Name -in $expected } |
        Select-Object Name, Length, @{ Name = 'SHA256'; Expression = { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash } } |
        Format-Table -AutoSize
} finally {
    if (Test-Path -LiteralPath $tempRoot) {
        Remove-Item -LiteralPath $tempRoot -Recurse -Force
    }
}
