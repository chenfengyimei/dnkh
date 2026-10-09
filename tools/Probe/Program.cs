using System.Text;
using Dnkhw.Core.Formatting;
using Dnkhw.Core.SystemCheck;

// 开发探针：真机调用 Dnkhw.Core 各服务，输出结果供人工核对。
// 用法: dotnet run --project tools/Probe -- [all|overview|checks]
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
