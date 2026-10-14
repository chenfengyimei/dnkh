using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace Dnkhw.Core.Update;

/// <summary>
/// 自替换支持：实际升级由独立进程在主程序退出后完成，避免 Windows 锁定运行中 exe；
/// 旧文件保留为短暂备份，替换失败时回滚。
/// </summary>
public static class SelfUpdater
{
    /// <summary>应用启动时调用：清理上次升级残留的 .old 与过期下载包（失败无害，下次再试）。</summary>
    public static void CleanupOldBinary()
    {
        var exePath = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exePath))
        {
            return;
        }

        Cleanup(exePath, UpdateDownloader.ReadyPath);
    }

    /// <summary>
    /// 用已下载并校验过的新版本替换当前 exe；替换成功后由调用方重启应用。
    /// </summary>
    public static void Apply(string readyPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readyPath);
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径");
        Apply(readyPath, exePath);
    }

    /// <summary>
    /// 下载前检查当前程序所在目录是否允许自更新。安装版默认位于当前用户目录；
    /// 免安装版可能被放进只读、受保护或无写权限的目录，需要在耗时下载前明确提示。
    /// </summary>
    public static void EnsureCurrentExecutableCanBeUpdated()
    {
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径");
        EnsureExecutableLocationWritable(exePath);
    }

    /// <summary>
    /// 启动独立更新器：等待当前进程完全退出后再替换 exe，避免 Windows 锁定运行中程序文件。
    /// </summary>
    public static void ScheduleApplyAfterExit(
        string readyPath,
        string expectedSha256,
        long expectedSize,
        string targetVersion,
        bool restartAfterApply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readyPath);
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径");
        if (!File.Exists(readyPath))
        {
            throw new FileNotFoundException("更新文件不存在", readyPath);
        }

        ValidateExpectedMetadata(expectedSha256, expectedSize, targetVersion);
        EnsureExecutableLocationWritable(exePath);

        Directory.CreateDirectory(UpdateDownloader.DownloadDirectory);
        // 用 -EncodedCommand 启动。Windows PowerShell 5.1 会把无 BOM 的 .ps1 按 ANSI 解析，
        // 含中文诊断文本时可能在真正替换前就语法失败；编码命令不经过脚本文件解码，可靠性更高。
        var encodedCommand = EncodePowerShellCommand(BuildHelperScript(
            Environment.ProcessId,
            exePath,
            readyPath,
            expectedSha256,
            expectedSize,
            targetVersion,
            restartAfterApply));

        var startInfo = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"),
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-NonInteractive");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-EncodedCommand");
        startInfo.ArgumentList.Add(encodedCommand);

        if (Process.Start(startInfo) is null)
        {
            throw new InvalidOperationException("无法启动更新器");
        }
    }

    internal static void Cleanup(string exePath, string readyPath)
    {
        try
        {
            var old = exePath + ".old";
            if (File.Exists(old))
            {
                File.Delete(old);
            }

            if (File.Exists(readyPath) && new FileInfo(readyPath).LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7))
            {
                // 未安装成功且超过一周的下载残留，清理
                File.Delete(readyPath);
            }
        }
        catch
        {
            // 清理失败完全无害：下次启动再试
        }
    }

    internal static void Apply(string readyPath, string exePath)
    {
        if (!File.Exists(readyPath))
        {
            throw new FileNotFoundException("更新文件不存在", readyPath);
        }

        var backup = exePath + ".old";
        var staged = exePath + ".update-new";
        TryDelete(staged);

        // ready 通常在系统盘的 LocalAppData，而免安装版可能位于其他盘或 U 盘。
        // 先复制到目标目录并二次校验，再做同目录改名，避免跨盘移动中断留下半个 EXE。
        try
        {
            File.Copy(readyPath, staged, overwrite: true);
            if (new FileInfo(readyPath).Length != new FileInfo(staged).Length
                || !string.Equals(HashFile(readyPath), HashFile(staged), StringComparison.Ordinal))
            {
                throw new InvalidDataException("更新文件复制校验失败，未替换当前程序");
            }
        }
        catch
        {
            TryDelete(staged);
            throw;
        }

        File.Move(exePath, backup, overwrite: true);
        try
        {
            File.Move(staged, exePath, overwrite: true);
        }
        catch
        {
            // 新文件落位失败：把旧文件放回去，绝不让用户处于无程序状态
            File.Move(backup, exePath, overwrite: true);
            throw;
        }

        TryDelete(readyPath);
        try
        {
            File.Delete(backup);
        }
        catch
        {
            // 旧文件删除失败不影响升级（下次启动 CleanupOldBinary 再清）
        }
    }

    internal static void EnsureExecutableLocationWritable(string exePath)
    {
        if (!File.Exists(exePath))
        {
            throw new FileNotFoundException("当前程序文件不存在", exePath);
        }

        if ((File.GetAttributes(exePath) & FileAttributes.ReadOnly) != 0)
        {
            throw new InvalidOperationException("当前程序为只读文件，请取消只读属性或移动到可写目录后再更新");
        }

        var directory = Path.GetDirectoryName(exePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new InvalidOperationException("无法确定当前程序所在目录");
        }

        var probe = Path.Combine(directory, $".dnkhw-update-probe-{Guid.NewGuid():N}.tmp");
        try
        {
            using var stream = new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            stream.WriteByte(0);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            throw new InvalidOperationException(
                "当前目录没有更新写入权限。免安装版请完整解压到可写目录，安装版请重新安装到当前用户目录后再更新。",
                ex);
        }
        finally
        {
            TryDelete(probe);
        }
    }

    /// <summary>PowerShell 的 -EncodedCommand 固定使用 UTF-16LE（Unicode）。</summary>
    internal static string EncodePowerShellCommand(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    internal static string BuildHelperScript(
        int processId,
        string exePath,
        string readyPath,
        string expectedSha256,
        long expectedSize,
        string targetVersion,
        bool restartAfterApply)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
        ValidateExpectedMetadata(expectedSha256, expectedSize, targetVersion);
        var restartLiteral = restartAfterApply ? "$true" : "$false";

        return $$"""
        $ErrorActionPreference = 'Stop'
        $target = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{Encode(exePath)}}'))
        $ready = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{Encode(readyPath)}}'))
        $expectedHash = '{{expectedSha256.Trim().ToUpperInvariant()}}'
        $expectedSize = {{expectedSize}}
        $targetVersion = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{Encode(targetVersion.Trim())}}'))
        $restart = {{restartLiteral}}
        $processId = {{processId}}
        $log = "$ready.apply.log"

        function Write-UpdateLog([string]$message) {
            try {
                $line = "$(Get-Date -Format o) $message"
                [IO.File]::AppendAllText($log, $line + [Environment]::NewLine, [Text.Encoding]::UTF8)
            } catch {
                # Logging must never stop the replacement attempt.
            }
        }

        function Get-FileSha256([string]$path) {
            $stream = [IO.File]::OpenRead($path)
            $sha = [Security.Cryptography.SHA256]::Create()
            try {
                $bytes = $sha.ComputeHash($stream)
                $builder = New-Object Text.StringBuilder
                foreach ($byte in $bytes) {
                    [void]$builder.Append($byte.ToString('X2'))
                }
                return $builder.ToString()
            } finally {
                $sha.Dispose()
                $stream.Dispose()
            }
        }

        Write-UpdateLog 'helper started'

        while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 200
        }

        if (-not (Test-Path -LiteralPath $ready)) {
            Write-UpdateLog 'ready package is missing (exit 2)'
            exit 2
        }

        try {
            $readyItem = Get-Item -LiteralPath $ready -ErrorAction Stop
            $readyHash = Get-FileSha256 $ready
            if ($readyItem.Length -ne $expectedSize -or $readyHash -ne $expectedHash) {
                Write-UpdateLog 'ready package metadata changed after download (exit 3)'
                exit 3
            }
        } catch {
            Write-UpdateLog ('ready package verification failed: ' + $_.Exception.Message)
            exit 3
        }

        $backup = "$target.old"
        $staged = "$target.update-new"
        try {
            # 缓存目录与免安装目录可能位于不同磁盘。先复制到目标目录并复核，
            # 再通过同目录改名落位，避免跨盘移动中断生成残缺程序。
            Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
            Copy-Item -LiteralPath $ready -Destination $staged -Force -ErrorAction Stop
            $stagedItem = Get-Item -LiteralPath $staged -ErrorAction Stop
            $stagedHash = Get-FileSha256 $staged
            if ($stagedItem.Length -ne $expectedSize -or $stagedHash -ne $expectedHash) {
                throw 'staged package verification failed'
            }
        } catch {
            Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
            Write-UpdateLog ('staging update package failed (exit 5): ' + $_.Exception.Message)
            exit 5
        }

        $applied = $false
        for ($attempt = 0; $attempt -lt 120; $attempt++) {
            try {
                # 上一次被杀毒软件/异常中断留下的备份不能阻塞后续升级；当前主程序仍在，
                # 因而在创建本次新备份前可以安全删除这个旧残留。
                if (Test-Path -LiteralPath $backup) {
                    Remove-Item -LiteralPath $backup -Force -ErrorAction Stop
                }
                Move-Item -LiteralPath $target -Destination $backup -Force -ErrorAction Stop
                try {
                    Move-Item -LiteralPath $ready -Destination $target -Force -ErrorAction Stop
                } catch {
                    Move-Item -LiteralPath $backup -Destination $target -Force -ErrorAction SilentlyContinue
                    throw
                }

                Remove-Item -LiteralPath $staged -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
                Remove-Item -LiteralPath $ready -Force -ErrorAction SilentlyContinue
                $applied = $true
                break
            } catch {
                Write-UpdateLog ("attempt $($attempt + 1) failed: " + $_.Exception.Message)
                Start-Sleep -Milliseconds 250
            }
        }

        if (-not $applied) {
            Write-UpdateLog 'replacement failed after 120 attempts (exit 1)'
            exit 1
        }

        # 安装版同步“已安装的应用”中的显示版本；免安装版没有对应键，会自然跳过。
        try {
            $targetDirectory = [IO.Path]::GetFullPath([IO.Path]::GetDirectoryName($target)).TrimEnd([IO.Path]::DirectorySeparatorChar)
            $uninstallKeys = @(
                'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7FBC00A-5F0F-44BB-8AA2-0CEB5ED6295A}_is1',
                'HKLM:\Software\Microsoft\Windows\CurrentVersion\Uninstall\{A7FBC00A-5F0F-44BB-8AA2-0CEB5ED6295A}_is1',
                'HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\{A7FBC00A-5F0F-44BB-8AA2-0CEB5ED6295A}_is1'
            )
            foreach ($key in $uninstallKeys) {
                if (-not (Test-Path -LiteralPath $key)) { continue }
                $installed = Get-ItemProperty -LiteralPath $key -Name InstallLocation -ErrorAction SilentlyContinue
                if ($null -eq $installed -or [string]::IsNullOrWhiteSpace($installed.InstallLocation)) { continue }
                $installedDirectory = [IO.Path]::GetFullPath($installed.InstallLocation).TrimEnd([IO.Path]::DirectorySeparatorChar)
                if ($installedDirectory -eq $targetDirectory) {
                    Set-ItemProperty -LiteralPath $key -Name DisplayVersion -Value $targetVersion -ErrorAction Stop
                    Write-UpdateLog "installed version metadata updated to $targetVersion"
                }
            }
        } catch {
            Write-UpdateLog ('installed version metadata update skipped: ' + $_.Exception.Message)
        }

        Write-UpdateLog 'replacement completed successfully'
        if ($restart) {
            try {
                Start-Process -FilePath $target -WorkingDirectory ([IO.Path]::GetDirectoryName($target)) -ErrorAction Stop
                Write-UpdateLog 'new version started successfully'
            } catch {
                Write-UpdateLog ('replacement succeeded but restart failed (exit 4): ' + $_.Exception.Message)
                exit 4
            }
        }
        exit 0
        """;
    }

    private static void ValidateExpectedMetadata(string expectedSha256, long expectedSize, string targetVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetVersion);
        var hash = expectedSha256.Trim();
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
        {
            throw new ArgumentException("更新包 SHA256 格式无效", nameof(expectedSha256));
        }

        if (expectedSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(expectedSize), "更新包大小必须大于 0");
        }
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
            // 临时文件会在下次尝试时继续清理
        }
    }
}
