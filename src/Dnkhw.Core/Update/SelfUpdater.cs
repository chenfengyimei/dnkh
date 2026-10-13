namespace Dnkhw.Core.Update;

/// <summary>
/// 就地自替换：Windows 允许重命名正在运行的 exe，
/// 「旧→.old、新→原名」两步即可无辅助进程完成升级；失败时回滚。
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
}
