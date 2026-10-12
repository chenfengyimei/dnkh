using Dnkhw.Core.Startup;
using Dnkhw.Core.Wallpaper;

namespace Dnkhw.Core.Tests.Startup;

public class ServiceStartupServiceTests
{
    [Theory]
    [InlineData("Running", "运行中")]
    [InlineData("Stopped", "已停止")]
    [InlineData("Start Pending", "启动中")]
    [InlineData("Stop Pending", "停止中")]
    [InlineData("Paused", "已暂停")]
    [InlineData("Whatever", "Whatever")]
    public void StateText_MapsKnownStates(string raw, string expected)
    {
        Assert.Equal(expected, ServiceStartupService.StateText(raw));
    }

    [Fact]
    public void StateText_TrimsWhitespace()
    {
        Assert.Equal("运行中", ServiceStartupService.StateText(" Running "));
    }
}
