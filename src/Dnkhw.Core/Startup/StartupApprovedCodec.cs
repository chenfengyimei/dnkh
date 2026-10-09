namespace Dnkhw.Core.Startup;

/// <summary>
/// StartupApproved 二进制值的编解码（12 字节：4 字节标志 + 8 字节 FILETIME）。
/// 实测取值：0x02=启用（其余为 0）；0x03=用户禁用+时间戳；0x01 等变体按 bit0 判定（奇数=禁用）。
/// 与任务管理器使用同一套标记，不删除原始 Run 键/快捷方式，完全可逆。
/// </summary>
public static class StartupApprovedCodec
{
    public const int ValueLength = 12;

    public static bool Decode(byte[]? value)
    {
        if (value is null || value.Length < 4)
        {
            return true; // 无记录视为启用
        }

        return (value[0] & 1) == 0;
    }

    public static byte[] Encode(bool enabled)
    {
        var value = new byte[ValueLength];
        value[0] = enabled ? (byte)0x02 : (byte)0x03;
        if (!enabled)
        {
            // 禁用时写入 8 字节当前 UTC FILETIME（与任务管理器一致）
            var time = (ulong)DateTime.UtcNow.ToFileTime();
            var bytes = BitConverter.GetBytes(time);
            bytes.CopyTo(value, 4);
        }

        return value;
    }
}
