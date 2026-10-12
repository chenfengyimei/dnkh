namespace Dnkhw.Services;

/// <summary>
/// fire-and-forget 安全包装：后台任务异常记录日志，而不是无人观察地丢失。
/// 用于 UI 事件驱动的后台刷新（磁盘扫描 / 详情加载等）。
/// </summary>
public static class SafeFireAndForget
{
    public static void Run(string context, Func<Task> work) => Run(context, work());

    /// <summary>async void 是 fire-and-forget 的唯一载体，方法体全程捕获，异常不可能逃逸。</summary>
    public static async void Run(string context, Task task)
    {
        try
        {
            await task;
        }
        catch (OperationCanceledException)
        {
            // 主动取消是正常流程（切盘 / 切文件夹），不按错误记录
        }
        catch (Exception ex)
        {
            LogService.Error($"{context} 后台任务失败", ex);
        }
    }
}
