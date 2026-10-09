using System.Text;
using Dnkhw.Core.AppCatalog;
using Dnkhw.Core.DiskScan;
using Dnkhw.Core.Downloads;
using Dnkhw.Core.Formatting;
using Dnkhw.Core.Optimize;
using Dnkhw.Core.Startup;
using Dnkhw.Core.SystemCheck;
using Dnkhw.Core.Wallpaper;
using Microsoft.Win32;

// 开发探针：真机调用 Dnkhw.Core 各服务，输出结果供人工核对。
// 用法: dotnet run --project tools/Probe -- [all|overview|checks|wallpaper|downloads|apps|startup|optimize|disk [路径]]
Console.OutputEncoding = Encoding.UTF8;

var service = new SystemCheckService();
var cmd = args.Length > 0 ? args[0].ToLowerInvariant() : "all";

if (cmd is "all" or "overview")
{
    Console.WriteLine("=== 系统概览 ===");
    var o = await service.GetOverviewAsync();
    Console.WriteLine($"系统: {o.OsName} {o.Edition} {o.DisplayVersion}（构建 {o.Build}）");
    Console.WriteLine($"类型: {o.ComputerType}，型号: {o.SystemModel}");
    Console.WriteLine($"主板: {o.BaseBoard}，BIOS: {o.BiosInfo}");
    Console.WriteLine($"安装日期: {o.InstallDate}");
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
    Console.WriteLine("=== 默认路径（六文件夹） ===");
    var migrator = new KnownFoldersMigrationService();
    foreach (var def in KnownFoldersMigrationService.Folders)
    {
        try
        {
            var s = migrator.GetStatus(def);
            Console.WriteLine(
                $"[{def.Name}] {s.CurrentPath}（{(s.IsRedirected ? "已重定向" : "系统默认")}）"
                + $" 内容 {s.FileCount:N0} 个文件 / {SizeFormatter.Format(s.TotalBytes)}"
                + (s.IsManagedByOneDrive ? " [OneDrive 接管]" : string.Empty));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{def.Name}] 读取失败: {ex.Message}");
        }
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

if (cmd is "all" or "optimize")
{
    Console.WriteLine("=== Windows 优化 ===");
    var optimizer = new OptimizeService();
    Console.WriteLine($"Win11: {optimizer.IsWindows11}");
    foreach (var i in optimizer.GetItems())
    {
        Console.WriteLine($"[{(optimizer.IsApplied(i.Id) ? "已应用" : "未应用")}] {i.Title}{(i.NeedsAdmin ? "（需管理员）" : "")}");
    }

    // 安全往返验证：advertisingId 应用→检测→恢复初始状态（保证净变化为零）
    var before = optimizer.IsApplied("advertisingId");
    optimizer.Apply("advertisingId");
    var appliedCheck = optimizer.IsApplied("advertisingId");
    Console.WriteLine($"advertisingId 应用后检测: {appliedCheck}（应为 True）");
    if (before)
    {
        optimizer.Apply("advertisingId");
    }
    else
    {
        optimizer.Revert("advertisingId");
    }
    var restored = optimizer.IsApplied("advertisingId");
    Console.WriteLine($"advertisingId 还原初始态: {restored}（应为 {before}）");
    Console.WriteLine(restored == before ? "往返验证通过：系统状态与初始一致" : "警告：往返后状态与初始不一致");
}

if (cmd is "all" or "disk")
{
    Console.WriteLine("=== 磁盘分析 ===");
    var root = args.Length > 1 ? args[1] : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Web");
    Console.WriteLine($"扫描: {root}");
    var scanner = new DiskScanner();
    var result = await scanner.ScanAsync(root, new Progress<long>(_ => { }));
    Console.WriteLine($"共 {result.ScannedFiles:N0} 个文件，{SizeFormatter.Format(result.ScannedBytes)}，跳过 {result.SkippedEntries:N0} 项，根目录散文件 {SizeFormatter.Format(result.RootLooseBytes)}");
    foreach (var f in result.TopFolders.Take(5))
    {
        Console.WriteLine($"  目录 {f.Path}  {SizeFormatter.Format(f.Bytes)}  {f.Percent}%  {f.FileCount} 文件");
    }

    foreach (var f in result.TopFiles.Take(3))
    {
        Console.WriteLine($"  文件 {f.Path}  {SizeFormatter.Format(f.Bytes)}");
    }

    foreach (var j in await scanner.GetJunkLocationsAsync())
    {
        Console.WriteLine($"  垃圾位 {j.Name}  {(j.Bytes > 0 ? SizeFormatter.Format(j.Bytes) : "—")}");
    }
}
