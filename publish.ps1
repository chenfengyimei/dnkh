# 电脑开荒发布脚本：产出自包含单文件 exe（约 50MB，目标机免装 .NET 运行时）
# Publish script: builds a self-contained single-file exe into ./publish
param(
    [string]$Output = "publish"
)

# 发布前删除旧产物：运行中的旧版会锁文件导致打包 CopyFile 静默失败
$target = Join-Path $Output "Dnkhw.exe"
if (Test-Path $target) {
    Remove-Item $target -Force
}

dotnet publish src/Dnkhw/Dnkhw.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
    -o $Output

if ($LASTEXITCODE -ne 0) {
    throw "publish failed (exit $LASTEXITCODE)"
}

if (-not (Test-Path $target)) {
    throw "publish output missing: $target"
}

Write-Host "Published: $target"
