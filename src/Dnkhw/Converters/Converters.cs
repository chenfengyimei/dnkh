using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Dnkhw.Converters;

/// <summary>null、空字符串或空集合折叠，否则显示。</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is null or string { Length: 0 } or System.Collections.ICollection { Count: 0 }
            ? Visibility.Collapsed
            : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Visibility.Visible;
}

/// <summary>数值为 null、0 或空集合时折叠（用于进度条等仅在有数据时显示的元素）。</summary>
public sealed class ZeroToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value switch
        {
            int i when i > 0 => Visibility.Visible,
            long l when l > 0 => Visibility.Visible,
            double d when d > 0 => Visibility.Visible,
            _ => Visibility.Collapsed,
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
