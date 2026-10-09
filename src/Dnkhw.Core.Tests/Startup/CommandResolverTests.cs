using Dnkhw.Core.Startup;

namespace Dnkhw.Core.Tests.Startup;

public class CommandResolverTests
{
    private static readonly HashSet<string> ExistsPaths = new(StringComparer.OrdinalIgnoreCase)
    {
        @"C:\Program Files\App\app.exe",
        @"C:\Windows\System32\rundll32.exe",
        @"C:\App\no quote.exe",
    };

    [Theory]
    [InlineData("\"C:\\Program Files\\App\\app.exe\" /background", @"C:\Program Files\App\app.exe")]
    [InlineData("C:\\Windows\\System32\\rundll32.exe shell32.dll,Control_RunDLL", @"C:\Windows\System32\rundll32.exe")]
    [InlineData("C:\\App\\no quote.exe --flag", @"C:\App\no quote.exe")]
    public void ResolveExecutable_FindsExistingPath(string command, string expected)
    {
        Assert.Equal(expected, CommandResolver.ResolveExecutable(command, p => ExistsPaths.Contains(p)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\"C:\\Missing\\app.exe\" /arg")]
    [InlineData("C:\\Missing\\app.exe --flag")]
    [InlineData("rundll32 xyz.dll,Entry")] // 无引号 rundll32 且不存在的完整 exe 名
    public void ResolveExecutable_MissingPath_ReturnsNull(string command)
    {
        Assert.Null(CommandResolver.ResolveExecutable(command, _ => false));
    }
}
