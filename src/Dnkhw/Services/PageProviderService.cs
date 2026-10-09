using System.Collections.Concurrent;
using Wpf.Ui.Abstractions;

namespace Dnkhw.Services;

/// <summary>
/// 导航页面提供器：按需创建页面实例并缓存，避免每次导航重建页面。
/// </summary>
public class PageProviderService : INavigationViewPageProvider
{
    private readonly ConcurrentDictionary<Type, object?> _pages = new();

    public object? GetPage(Type pageType)
    {
        return _pages.GetOrAdd(pageType, static t => Activator.CreateInstance(t));
    }
}
