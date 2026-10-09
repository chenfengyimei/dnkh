namespace Dnkhw.Core.Startup;

/// <summary>
/// 从启动命令行解析主程序完整路径（供发布者签名信息使用）。fileExists 可注入以便单测。
/// </summary>
public static class CommandResolver
{
    public static string? ResolveExecutable(string command, Func<string, bool>? fileExists = null)
    {
        fileExists ??= File.Exists;

        command = command.Trim();
        if (command.Length == 0)
        {
            return null;
        }

        // 带引号形式："C:\Program Files\App\app.exe" /参数
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            var path = end > 1 ? command[1..end] : command[1..];
            return fileExists(path) ? path : null;
        }

        // 无引号形式：按空格累积候选路径，命中存在的文件即返回
        var candidate = string.Empty;
        foreach (var part in command.Split(' '))
        {
            candidate = candidate.Length == 0 ? part : candidate + " " + part;
            if (fileExists(candidate))
            {
                return candidate;
            }

            if (candidate.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return null; // exe 全名后仍不存在则放弃，避免误判后续参数
            }
        }

        return null;
    }
}
