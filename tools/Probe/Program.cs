using System.Text;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;
using Dnkhw.Core.Startup;
using Dnkhw.Core.SystemCheck;
using Dnkhw.Core.Wallpaper;
using Microsoft.Win32;

// 开发探针：真机调用 Dnkhw.Core 各服务，输出结果供人工核对。
// 用法: dotnet run --project tools/Probe -- [all|overview|checks|wallpaper|downloads|apps|startup]
Console.OutputEncoding = Encoding.UTF8;

var service = new SystemCheckService();
var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

if (cmd is "all" or "overview")
{
    Console.WriteLine("=== 系统概览 ===");
    var o = await service.GetOverviewAsync();
    Console.WriteLine($"系统: {o.OsName} {o.Edition} {o.DisplayVersion}（构建 {o.Build}）");
    Console.WriteLine($"CPU: {o.Cpu}");
    Console.WriteLine($"内存: {SizeFormatter.Format(o.RamBytes)}");
    Console.WriteLine($"显卡: {o.Gpu}");
    Console.WriteLine($"分辨率: {o.Resolution}");
    Console.WriteLine($"主机名: {o.HostName}，开机时长: {o.Uptime}");
    foreach (var d in o.Disks)
    {
        Console.WriteLine($"  {d.Root} {d.Label}：总 {SizeFormatter.Format(d.TotalBytes)}，剩 {SizeFormatter.Format(d.FreeBytes)}");
    }
}

if (cmd is "all" or "checks")
{
    Console.WriteLine("=== 检查项 ===");
    foreach (var item in await service.RunChecksAsync())
    {
        var suffix = item.Suggestion is null ? string.Empty : $"　→ {item.Suggestion}";
        Console.WriteLine($"[{item.Status,4}] {item.Title}: {item.Detail}{suffix}");
    }
}

if (cmd is "all" or "wallpaper")
{
    Console.WriteLine("=== 壁纸 ===");
    var bing = new BingWallpaperService();
    try
    {
        foreach (var w in await bing.GetRecentAsync(8))
        {
            Console.WriteLine($"{w.EndDate} {w.Title}  文件={w.FullFileName}");
        }
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Bing 接口失败: {ex.Message}");
    }

    var current = WallpaperService.GetCurrent();
    Console.WriteLine($"当前壁纸: {current}");
    if (File.Exists(current))
    {
        // 安全验证：用同一张图重走 SPI 设置流程（视觉上无变化）
        WallpaperService.Set(current, WallpaperFit.Fill);
        Console.WriteLine("SPI_SETDESKWALLPAPER 重应用当前壁纸成功");
    }
}

if (cmd is "all" or "downloads")
{
    Console.WriteLine("=== 下载路径 ===");
    var migrator = new DownloadsMigrationService();
    var s = migrator.GetStatus();
    Console.WriteLine($"当前: {s.CurrentPath}（{(s.IsRedirected ? "已重定向" : "系统默认")}）");
    Console.WriteLine($"内容: {s.SourceFileCount} 个文件，{SizeFormatter.Format(s.SourceTotalBytes)}");
    foreach (var d in s.Drives)
    {
        Console.WriteLine($"  盘 {d.Root} {d.Label}：剩 {SizeFormatter.Format(d.FreeBytes)}");
    }

    foreach (var b in s.Browsers)
    {
        Console.WriteLine($"  {b.Name}: 安装={b.IsInstalled}，跟随系统={b.UsesSystemDefault}" + (b.CustomDir is null ? string.Empty : $"，自定义={b.CustomDir}"));
    }
}

if (cmd is "all" or "apps")
{
    Console.WriteLine("=== APP 推荐 ===");
    var winget = new WingetInstaller();
    Console.WriteLine($"winget 可用: {winget.IsWingetAvailable()}");
    var data = AppCatalogLoader.LoadDefault();
    var installed = InstallDetector.GetInstalledDisplayNames();
    foreach (var c in data.Categories)
    {
        foreach (var a in c.Apps)
        {
            Console.WriteLine($"[{(InstallDetector.IsInstalled(a.Name, installed) ? "已装" : "  ")}] {a.Name,-18} ({a.WingetId ?? "官网直链"})");
        }
    }

    // 无副作用验证包解析：winget show 只查询不安装
    var show = await winget.RunAsync("show --id 7zip.7zip -e");
    Console.WriteLine($"winget show 7-Zip: exit={show.ExitCode}，首行={show.Output.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0)}");

    // 失败路径验证：不存在的包应返回非零退出码与可读错误
    var fail = await winget.RunAsync("install --id DNKHW.DoesNotExist -e --silent --accept-package-agreements --accept-source-agreements");
    Console.WriteLine($"winget 失败路径: exit={fail.ExitCode}");
    Console.WriteLine("  " + fail.Output.Split('\r', '\n').FirstOrDefault(l => l.Trim().Length > 0));
}

if (cmd is "all" or "startup")
{
    Console.WriteLine("=== 启动项 ===");
    var startup = new StartupService();
    foreach (var i in startup.GetItems())
    {
        Console.WriteLine($"[{(i.IsEnabled ? "启用" : "禁用")}] {i.LocationText} | {i.Name} | {i.Command}{(i.Publisher is null ? " | 无签名" : $" | {i.Publisher}")}");
    }

    // codec 真实读写验证：孤儿 StartupApproved 条目（无对应 Run 项，系统忽略），写→读→还原→清理
    using (var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"))
    {
        key.SetValue("dnkhw_probe_orphan", StartupApprovedCodec.Encode(false), RegistryValueKind.Binary);
        var raw = (byte[]?)key.GetValue("dnkhw_probe_orphan");
        Console.WriteLine($"codec 禁用写入: {Convert.ToHexString(raw!)} → 解码启用={StartupApprovedCodec.Decode(raw)}（应为 False）");
        key.SetValue("dnkhw_probe_orphan", StartupApprovedCodec.Encode(true), RegistryValueKind.Binary);
        raw = (byte[]?)key.GetValue("dnkhw_probe_orphan");
        Console.WriteLine($"codec 启用写入: {Convert.ToHexString(raw!)} → 解码启用={StartupApprovedCodec.Decode(raw)}（应为 True）");
        key.DeleteValue("dnkhw_probe_orphan");
    }
    Console.WriteLine("codec 孤儿条目已清理");
}
