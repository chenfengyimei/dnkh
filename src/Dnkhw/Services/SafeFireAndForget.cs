namespace Dnkhw.Services;

/// <summary>
/// fire-and-forget 安全包装：后台任务异常记录日志，而不是无人观察地丢失。
/// 用于 UI 事件驱动的后台刷新（磁盘扫描 / 详情加载等）。
/// </summary>
public static class SafeFireAndForget
{
    /// <summary>连任务委托的同步启动阶段也纳入捕获，避免异常在返回 Task 前逃逸到 UI 线程。</summary>
    public static async void Run(string context, Func<Task> work)
    {
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            LogService.Info($"{context} 已取消");
        }
        catch (Exception ex)
        {
            LogService.Error($"{context} 后台任务失败", ex);
        }
    }

    /// <summary>async void 是 fire-and-forget 的唯一载体，方法体全程捕获，异常不可能逃逸。</summary>
    public static void Run(string context, Task task) => Run(context, () => task);
}
