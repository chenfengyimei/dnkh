namespace Dnkhw.Core.SystemCheck;

/// <summary>
/// 判断 HKLM\SYSTEM\CurrentControlSet\Control\Session Manager 的 PendingFileRenameOperations 是否有待重启操作。
/// 该值成对记录待重命名的文件（源、目标）；首对通常是历史残留，其后仍存在非空条目时视为有待重启操作。
/// 注册表中实际类型为 REG_MULTI_SZ（string[]，每个数组元素一条记录）；导出/导入的 .reg 场景为含 \0 分隔的 REG_SZ。
/// </summary>
public static class PendingFileRename
{
    public static bool HasPendingOperations(object? rawValue)
    {
        var parts = rawValue switch
        {
            null => [],
            string s => s.Split('\0'),
            string[] arr => arr,
            _ => [],
        };

        return parts.Length > 2 && parts.Skip(2).Any(p => !string.IsNullOrWhiteSpace(p));
    }
}
