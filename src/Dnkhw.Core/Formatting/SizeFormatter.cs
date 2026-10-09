namespace Dnkhw.Core.Formatting;

/// <summary>
/// 将字节数格式化为可读大小（如 1.5 GB）。
/// </summary>
public static class SizeFormatter
{
    private static readonly string[] Units = ["B", "KB", "MB", "GB", "TB", "PB"];

    public static string Format(double bytes)
    {
        if (double.IsNaN(bytes) || bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes));
        }

        if (bytes < 1024d)
        {
            return $"{bytes:0} B";
        }

        double value = bytes;
        int unit = 0;
        while (value >= 1024d && unit < Units.Length - 1)
        {
            value /= 1024d;
            unit++;
        }

        return $"{value:0.##} {Units[unit]}";
    }
}
