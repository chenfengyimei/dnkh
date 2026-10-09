using Dnkhw.Core.SystemCheck;

namespace Dnkhw.Core.Tests.SystemCheck;

public class CheckReportBuilderTests
{
    private static readonly SystemOverview Overview = new(
        "Windows 11", "专业版", "24H2", "26100.1742",
        "笔记本", "ASUS Test Model", "Test Board", "Test BIOS 1.0",
        "Test CPU（8 核 16 线程）", "Test GPU", 34359738368, "1920 × 1080",
        "TEST-PC", "2 天 3 小时", "2024-05-01",
        [new DiskInfo(@"C:\", "系统盘", 512000000000, 234000000000)]);

    [Fact]
    public void BuildMarkdown_ContainsOverviewAndItems()
    {
        var items = new List<CheckItem>
        {
            new("activation", "系统激活", CheckStatus.Ok, "Windows 已激活"),
            new("sysdisk", "系统盘空间", CheckStatus.Warn, @"系统盘 C:\ 仅剩 15 GB", "建议清理", "downloads"),
            new("power", "电源计划", CheckStatus.Info, "当前电源计划：平衡"),
        };

        var md = CheckReportBuilder.BuildMarkdown(Overview, items);

        Assert.Contains("电脑开荒检查报告", md);
        Assert.Contains("Windows 11", md);
        Assert.Contains("TEST-PC", md);
        Assert.Contains("C:\\", md);
        Assert.Contains("正常", md);
        Assert.Contains("注意", md);
        Assert.Contains("信息", md);
        Assert.Contains("建议清理", md);
    }

    [Fact]
    public void BuildMarkdown_EscapesPipesInCell()
    {
        var items = new List<CheckItem>
        {
            new("x", "带竖线|的详情", CheckStatus.Ok, "值 a|b"),
        };

        var md = CheckReportBuilder.BuildMarkdown(Overview, items);

        Assert.DoesNotContain("值 a|b", md);
        Assert.Contains(@"值 a\|b", md);
    }
}
