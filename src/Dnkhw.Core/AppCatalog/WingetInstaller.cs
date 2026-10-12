using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Dnkhw.Core.AppCatalog;

public sealed record WingetResult(bool Success, int ExitCode, string Output);

/// <summary>
/// winget 静默安装封装。--silent 全静默安装，逐条返回完整输出便于排错。
/// </summary>
public sealed partial class WingetInstaller
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    /// <summary>winget.exe 的用户级安装位置（App Installer 每用户安装到 WindowsApps）。</summary>
    public static string ExecutablePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Microsoft",
        "WindowsApps",
        "winget.exe");

    private static string WingetPath => ExecutablePath;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,199}$", RegexOptions.CultureInvariant)]
    private static partial Regex WingetIdRegex();

    internal static bool IsValidWingetId(string? wingetId)
        => wingetId is not null && WingetIdRegex().IsMatch(wingetId);

    public bool IsWingetAvailable()
    {
        try
        {
            var psi = CreateStartInfo(["--version"]);
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds))
            {
                TryKill(process);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>安装指定 winget 包（--silent 全静默），失败时返回完整输出便于排错。</summary>
    public Task<WingetResult> InstallAsync(string wingetId, CancellationToken ct = default)
    {
        if (!IsValidWingetId(wingetId))
        {
            throw new ArgumentException("winget 包标识格式无效", nameof(wingetId));
        }

        return RunAsync(
            ["install", "--id", wingetId, "-e", "--silent", "--accept-package-agreements", "--accept-source-agreements", "--disable-interactivity"],
            ct);
    }

    /// <summary>执行任意 winget 命令（探针用于 winget show / 失败路径验证）。</summary>
    internal async Task<WingetResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct = default)
    {
        using var process = Process.Start(CreateStartInfo(arguments))
            ?? throw new InvalidOperationException("无法启动 winget 进程");

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(DefaultTimeout);

        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeoutCts.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeoutCts.Token);
            await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            var output = string.Join(Environment.NewLine,
                new[]
                {
                    CleanOutput(await outputTask.ConfigureAwait(false)),
                    CleanOutput(await errorTask.ConfigureAwait(false)),
                }.Where(s => s.Length > 0));
            return new WingetResult(process.ExitCode == 0, process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            try
            {
                await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // 进程已退出或终止失败，保留原取消/超时语义。
            }

            ct.ThrowIfCancellationRequested();
            throw new TimeoutException($"winget 执行超过 {DefaultTimeout.TotalMinutes:0} 分钟，已终止");
        }
    }

    private static ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(WingetPath)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch
        {
            // 可用性检查/取消路径不能被终止竞态覆盖。
        }
    }

    /// <summary>winget 进度条使用 \r 刷新，压成可读文本。</summary>
    private static string CleanOutput(string output)
    {
        var lines = output.Split('\r', '\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .Distinct();
        return string.Join(Environment.NewLine, lines);
    }
}
