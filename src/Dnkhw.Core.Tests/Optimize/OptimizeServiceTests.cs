using Dnkhw.Core.Optimize;

namespace Dnkhw.Core.Tests.Optimize;

public class OptimizeServiceTests
{
    [Fact]
    public void GetItems_MetadataIsCompleteAndUnique()
    {
        var service = new OptimizeService();
        var items = service.GetItems();

        Assert.True(items.Count >= 6);
        var ids = new HashSet<string>();
        foreach (var item in items)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.Id));
            Assert.False(string.IsNullOrWhiteSpace(item.Title));
            Assert.False(string.IsNullOrWhiteSpace(item.Description));
            Assert.True(ids.Add(item.Id), $"优化项 Id 重复：{item.Id}");
        }
    }

    [Fact]
    public void GetItems_Win11HidesWin10OnlyItems()
    {
        var service = new OptimizeService();
        var items = service.GetItems();

        if (service.IsWindows11)
        {
            Assert.DoesNotContain(items, i => i.Id == "searchBing");
            Assert.Contains(items, i => i.Id == "startRecommendations");
        }
        else
        {
            Assert.Contains(items, i => i.Id == "searchBing");
            Assert.DoesNotContain(items, i => i.Id == "startRecommendations");
        }
    }

    [Fact]
    public void GetItems_AdminFlagsAreConsistent()
    {
        var service = new OptimizeService();

        // 遥测项需要管理员；电源计划切换不需要
        Assert.Contains(service.GetItems(), i => i.Id == "telemetry" && i.NeedsAdmin);
        Assert.Contains(service.GetItems(), i => i.Id == "power" && !i.NeedsAdmin);
    }

    [Fact]
    public void Apply_UnknownId_Throws()
    {
        var service = new OptimizeService();
        Assert.Throws<ArgumentOutOfRangeException>(() => service.Apply("no-such-id"));
    }
}
