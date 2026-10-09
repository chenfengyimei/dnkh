using Dnkhw.Core.Startup;

namespace Dnkhw.Core.Tests.Startup;

public class StartupApprovedCodecTests
{
    [Fact]
    public void EncodeEnabled_MatchesTaskManagerFormat()
    {
        var value = StartupApprovedCodec.Encode(true);

        Assert.Equal(12, value.Length);
        Assert.Equal(0x02, value[0]);
        Assert.All(value[1..], b => Assert.Equal(0, b));
    }

    [Fact]
    public void EncodeDisabled_HasFlagAndTimestamp()
    {
        var value = StartupApprovedCodec.Encode(false);

        Assert.Equal(0x03, value[0]);
        Assert.Contains(value[4..], b => b != 0); // FILETIME 非零
    }

    [Theory]
    [InlineData(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]   // 任务管理器「启用」
    [InlineData(new byte[] { 0x03, 0x66, 0xFF, 0x6F, 0x36, 0x59, 0xDC, 0xDB, 0x01, 0, 0, 0 }, false)] // 真机 Steam「禁用」样本
    [InlineData(new byte[] { 0x01, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, false)]   // 真机 Docker Desktop 变体
    [InlineData(new byte[] { 0x06, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, true)]    // 偶见变体：bit0=0 视为启用
    public void Decode_RealWorldSamples(byte[] value, bool expected)
    {
        Assert.Equal(expected, StartupApprovedCodec.Decode(value));
    }

    [Fact]
    public void Decode_MissingOrShortValue_TreatedAsEnabled()
    {
        Assert.True(StartupApprovedCodec.Decode(null));
        Assert.True(StartupApprovedCodec.Decode(new byte[] { 0x02, 0, 0 }));
    }

    [Fact]
    public void EncodeDecode_Roundtrip()
    {
        var disabled = StartupApprovedCodec.Encode(false);
        Assert.False(StartupApprovedCodec.Decode(disabled));

        var enabled = StartupApprovedCodec.Encode(true);
        Assert.True(StartupApprovedCodec.Decode(enabled));
    }
}
