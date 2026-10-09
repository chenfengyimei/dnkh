using System.Text;
using Dnkhw.Core.Formatting;

namespace Dnkhw.Core.SystemCheck;

/// <summary>
/// 生成 Markdown 格式的检查报告。
/// </summary>
public static class CheckReportBuilder
{
    public static string BuildMarkdown(SystemOverview overview, IReadOnlyList<CheckItem> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# 电脑开荒检查报告");
        sb.AppendLine();
        sb.AppendLine($"- 生成时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"- 系统：{overview.OsName} {overview.Edition} {overview.DisplayVersion}（构建 {overview.Build}）");
        sb.AppendLine($"- CPU：{overview.Cpu}");
        sb.AppendLine($"- 内存：{SizeFormatter.Format(overview.RamBytes)}");
        sb.AppendLine($"- 显卡：{overview.Gpu}");
        sb.AppendLine($"- 分辨率：{overview.Resolution}");
        sb.AppendLine($"- 主机名：{overview.HostName}");
        sb.AppendLine();

        sb.AppendLine("## 分区");
        sb.AppendLine();
        sb.AppendLine("| 盘符 | 卷标 | 总容量 | 剩余 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var d in overview.Disks)
        {
            sb.AppendLine($"| {d.Root} | {Cell(d.Label)} | {SizeFormatter.Format(d.TotalBytes)} | {SizeFormatter.Format(d.FreeBytes)} |");
        }
        sb.AppendLine();

        sb.AppendLine("## 检查项");
        sb.AppendLine();
        sb.AppendLine("| 状态 | 项目 | 详情 | 建议 |");
        sb.AppendLine("|---|---|---|---|");
        foreach (var i in items)
        {
            var status = i.Status switch
            {
                CheckStatus.Ok => "正常",
                CheckStatus.Warn => "注意",
                _ => "信息",
            };
            sb.AppendLine($"| {status} | {Cell(i.Title)} | {Cell(i.Detail)} | {Cell(i.Suggestion)} |");
        }
        sb.AppendLine();

        return sb.ToString();
    }

    private static string Cell(string? text)
        => (text ?? string.Empty).Replace("\r", string.Empty).Replace("\n", " ").Replace("|", @"\|");
}
