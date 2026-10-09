using System.Diagnostics;
using System.Text;

namespace Dnkhw.Core.AppCatalog;

public sealed record WingetResult(bool Success, int ExitCode, string Output);

/// <summary>
/// winget 静默安装封装。--silent 全静默安装，逐条返回完整输出便于排错。
/// </summary>
public sealed class WingetInstaller
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(15);

    public bool IsWingetAvailable()
    {
        try
        {
            var psi = CreateStartInfo("--version");
            using var process = Process.Start(psi);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit((int)TimeSpan.FromSeconds(10).TotalMilliseconds))
            {
                process.Kill(entireProcessTree: true);
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
        => RunAsync(
            $"install --id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements --disable-interactivity",
            ct);

    /// <summary>执行任意 winget 命令（探针用于 winget show / 失败路径验证）。</summary>
    internal async Task<WingetResult> RunAsync(string arguments, CancellationToken ct = default)
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
            var output = CleanOutput(await outputTask.ConfigureAwait(false)) + CleanOutput(await errorTask.ConfigureAwait(false));
            return new WingetResult(process.ExitCode == 0, process.ExitCode, output);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(string arguments) => new("winget", arguments)
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8,
    };

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
