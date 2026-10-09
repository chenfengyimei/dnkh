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

    [Fact]
    public void GetGroups_GroupsItemsConsistently()
    {
        var service = new OptimizeService();
        var groups = service.GetGroups();

        // 五个分组，与平铺列表数量一致，组内 GroupId/GroupName 一致
        Assert.Equal(5, groups.Count);
        var allItems = groups.SelectMany(g => g.Items).ToList();
        Assert.Equal(service.GetItems().Count, allItems.Count);

        foreach (var group in groups)
        {
            Assert.NotEmpty(group.Items);
            Assert.All(group.Items, i => Assert.Equal(group.Id, i.GroupId));
            Assert.All(group.Items, i => Assert.Equal(group.Name, i.GroupName));
        }
    }

    [Fact]
    public void GetGroups_EveryGroupHasItems()
    {
        var service = new OptimizeService();
        var groups = service.GetGroups();

        // 本机（Win11）应包含全部五组
        if (service.IsWindows11)
        {
            Assert.Contains(groups, g => g.Id == "promo" && g.Items.Count >= 5);
            Assert.Contains(groups, g => g.Id == "search" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "notifications" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "privacy" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "performance" && g.Items.Count >= 2);
        }
    }
}
