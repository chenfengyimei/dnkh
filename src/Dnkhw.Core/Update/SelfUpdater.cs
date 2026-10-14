using System.Diagnostics;
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
    /// 启动独立更新器：等待当前进程完全退出后再替换 exe，避免 Windows 锁定运行中程序文件。
    /// </summary>
    public static void ScheduleApplyAfterExit(string readyPath, bool restartAfterApply)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(readyPath);
        var exePath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前程序路径");
        if (!File.Exists(readyPath))
        {
            throw new FileNotFoundException("更新文件不存在", readyPath);
        }

        Directory.CreateDirectory(UpdateDownloader.DownloadDirectory);
        // 用 -EncodedCommand 启动。Windows PowerShell 5.1 会把无 BOM 的 .ps1 按 ANSI 解析，
        // 含中文诊断文本时可能在真正替换前就语法失败；编码命令不经过脚本文件解码，可靠性更高。
        var encodedCommand = EncodePowerShellCommand(BuildHelperScript(
            Environment.ProcessId,
            exePath,
            readyPath,
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
        File.Move(exePath, backup, overwrite: true);
        try
        {
            File.Move(readyPath, exePath);
        }
        catch
        {
            // 新文件落位失败：把旧文件放回去，绝不让用户处于无程序状态
            File.Move(backup, exePath, overwrite: true);
            throw;
        }

        try
        {
            File.Delete(backup);
        }
        catch
        {
            // 旧文件删除失败不影响升级（下次启动 CleanupOldBinary 再清）
        }
    }

    /// <summary>PowerShell 的 -EncodedCommand 固定使用 UTF-16LE（Unicode）。</summary>
    internal static string EncodePowerShellCommand(string script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(script);
        return Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
    }

    internal static string BuildHelperScript(int processId, string exePath, string readyPath, bool restartAfterApply)
    {
        static string Encode(string value) => Convert.ToBase64String(Encoding.Unicode.GetBytes(value));
        var restartLiteral = restartAfterApply ? "$true" : "$false";

        return $$"""
        $ErrorActionPreference = 'Stop'
        $target = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{Encode(exePath)}}'))
        $ready = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String('{{Encode(readyPath)}}'))
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

        Write-UpdateLog 'helper started'

        while (Get-Process -Id $processId -ErrorAction SilentlyContinue) {
            Start-Sleep -Milliseconds 200
        }

        if (-not (Test-Path -LiteralPath $ready)) {
            Write-UpdateLog 'ready package is missing (exit 2)'
            exit 2
        }

        $backup = "$target.old"
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

                Remove-Item -LiteralPath $backup -Force -ErrorAction SilentlyContinue
                if ($restart) {
                    Start-Process -FilePath $target
                }
                Write-UpdateLog 'replacement completed successfully'
                exit 0
            } catch {
                Write-UpdateLog ("attempt $($attempt + 1) failed: " + $_.Exception.Message)
                Start-Sleep -Milliseconds 250
            }
        }

        Write-UpdateLog 'replacement failed after 120 attempts (exit 1)'
        exit 1
        """;
    }
}
