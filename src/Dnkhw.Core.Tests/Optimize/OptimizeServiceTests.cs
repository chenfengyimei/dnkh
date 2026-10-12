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

        // 六个分组，与平铺列表数量一致，组内 GroupId/GroupName 一致
        Assert.Equal(6, groups.Count);
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

        // 本机（Win11）应包含全部六组
        if (service.IsWindows11)
        {
            Assert.Contains(groups, g => g.Id == "promo" && g.Items.Count >= 5);
            Assert.Contains(groups, g => g.Id == "search" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "notifications" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "privacy" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "performance" && g.Items.Count >= 2);
            Assert.Contains(groups, g => g.Id == "ui" && g.Items.Any(i => i.Id == "classicContextMenu"));
        }
    }

    [Fact]
    public void Revert_ClassicContextMenu_DeletesShellKeyWithoutBackupPayload()
    {
        var service = new OptimizeService();

        // 经典右键菜单是「键存在/删除」式动作，无注册表值备份载荷：
        // Revert 走动作自身的恢复（不会因空备份而跳过）
        var item = service.GetItems().FirstOrDefault(i => i.Id == "classicContextMenu");
        if (item is null)
        {
            // Win10 上该项隐藏，跳过
            return;
        }

        service.Apply("classicContextMenu");
        try
        {
            Assert.True(service.IsApplied("classicContextMenu"));
        }
        finally
        {
            service.Revert("classicContextMenu");
            Assert.False(service.IsApplied("classicContextMenu"));
        }
    }
}
