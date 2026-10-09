using Dnkhw.Core.SystemCheck;

namespace Dnkhw.Core.Tests.SystemCheck;

public class PendingFileRenameTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("a\0b\0")]          // REG_SZ：仅首对（历史残留）
    [InlineData("x\0\0")]           // REG_SZ：首对后无有效条目
    public void HasPendingOperations_False(string? value)
    {
        Assert.False(PendingFileRename.HasPendingOperations(value));
    }

    [Theory]
    [InlineData("a\0b\0c\0d\0")]     // REG_SZ：首对之后仍有非空条目
    [InlineData("a\0b\0c")]          // REG_SZ：尾部无 \0 但存在第三条目
    public void HasPendingOperations_True(string value)
    {
        Assert.True(PendingFileRename.HasPendingOperations(value));
    }

    [Fact]
    public void HasPendingOperations_RegMultiSz()
    {
        // 注册表真实类型 REG_MULTI_SZ：每个元素一条记录
        Assert.False(PendingFileRename.HasPendingOperations(new[] { "a", "b" }));
        Assert.False(PendingFileRename.HasPendingOperations(new[] { "x", "" }));
        Assert.True(PendingFileRename.HasPendingOperations(new[] { "a", "b", "c", "d" }));
        Assert.True(PendingFileRename.HasPendingOperations(new[] { "a", "b", "c" }));
    }
}
