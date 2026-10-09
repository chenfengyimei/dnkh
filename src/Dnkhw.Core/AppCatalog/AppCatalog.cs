using System.Reflection;
using System.Text.Json;

namespace Dnkhw.Core.AppCatalog;

/// <summary>
/// 精选软件清单加载器（编译进程序集的 apps.json）。收录标准：官方渠道可获取、无捆绑安装器、无弹窗广告。
/// </summary>
public static class AppCatalogLoader
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
    };

    public static AppData LoadDefault()
    {
        var assembly = typeof(AppCatalogLoader).Assembly;
        var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("apps.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("未找到嵌入资源 apps.json");
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException("读取嵌入资源失败");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static AppData Parse(string json)
    {
        var data = JsonSerializer.Deserialize<AppData>(json, Options)
            ?? throw new InvalidOperationException("apps.json 解析失败");

        // 清单完整性校验：名称/官网非空、官网合法、类别与应用名唯一
        var categoryIds = new HashSet<string>();
        var appNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var wingetIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in data.Categories)
        {
            if (string.IsNullOrWhiteSpace(category.Id) || string.IsNullOrWhiteSpace(category.Name))
            {
                throw new InvalidOperationException("apps.json 存在缺少 Id/Name 的类别");
            }

            if (!categoryIds.Add(category.Id))
            {
                throw new InvalidOperationException($"类别 Id 重复：{category.Id}");
            }

            foreach (var app in category.Apps)
            {
                if (string.IsNullOrWhiteSpace(app.Name) || string.IsNullOrWhiteSpace(app.Homepage))
                {
                    throw new InvalidOperationException($"apps.json 存在缺少 Name/Homepage 的条目（类别 {category.Id}）");
                }

                if (!Uri.TryCreate(app.Homepage, UriKind.Absolute, out var uri) || uri.Scheme != "https")
                {
                    throw new InvalidOperationException($"条目 {app.Name} 的 Homepage 必须是合法 https 链接");
                }

                if (!appNames.Add(app.Name))
                {
                    throw new InvalidOperationException($"应用名重复：{app.Name}");
                }

                if (app.WingetId is not null && !wingetIds.Add(app.WingetId))
                {
                    throw new InvalidOperationException($"wingetId 重复：{app.WingetId}");
                }
            }
        }

        return data;
    }
}
