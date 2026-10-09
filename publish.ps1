# 电脑开荒发布脚本：产出自包含单文件 exe（约 50MB，目标机免装 .NET 运行时）
# Publish script: builds a self-contained single-file exe into ./publish
param(
    [string]$Output = "publish"
)

dotnet publish src/Dnkhw/Dnkhw.csproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=embedded `
    -o $Output

Write-Host "Published: $Output\Dnkhw.exe"
