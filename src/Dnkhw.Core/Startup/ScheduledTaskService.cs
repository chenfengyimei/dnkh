namespace Dnkhw.Core.Startup;

public sealed record ScheduledTaskInfo(
    string Name,
    string Path,
    string StateText,
    string TriggerText,
    string ActionText);

/// <summary>
/// 计划任务枚举（只读）：通过 Schedule.Service COM 列出带「登录触发器」的任务，
/// 与任务管理器「启动应用」页同类内容。不提供禁用——系统任务误禁用是重灾区，
/// 统一引导用户去任务管理器/设置操作。
/// </summary>
public static class ScheduledTaskService
{
    private const int TriggerTypeLogon = 9;
    private const int ActionTypeExec = 0;

    public static IReadOnlyList<ScheduledTaskInfo> GetLogonTriggeredTasks()
    {
        var results = new List<ScheduledTaskInfo>();
        var type = Type.GetTypeFromProgID("Schedule.Service");
        if (type is null)
        {
            return results;
        }

        dynamic service = Activator.CreateInstance(type)
            ?? throw new InvalidOperationException("无法创建任务计划程序 COM 实例");
        service.Connect();

        CollectFolder((dynamic)service.GetFolder("\\"), results);

        // 用户任务在前，\Microsoft\ 系统任务在后，各自按路径排序
        return results
            .OrderBy(t => t.Path.StartsWith(@"\Microsoft\", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(t => t.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static void CollectFolder(dynamic folder, List<ScheduledTaskInfo> results)
    {
        foreach (dynamic task in folder.GetTasks(0))
        {
            try
            {
                if (!HasLogonTrigger(task))
                {
                    continue;
                }

                results.Add(new ScheduledTaskInfo(
                    Name: (string)task.Name,
                    Path: (string)task.Path,
                    StateText: StateText((int)task.State),
                    TriggerText: DescribeTriggers(task),
                    ActionText: DescribeActions(task)));
            }
            catch
            {
                // 个别系统任务的元数据受 ACL 保护，读取失败即跳过
            }
        }

        foreach (dynamic subFolder in folder.GetFolders(0))
        {
            CollectFolder(subFolder, results);
        }
    }

    private static bool HasLogonTrigger(dynamic task)
    {
        foreach (dynamic trigger in task.Definition.Triggers)
        {
            if ((int)trigger.Type == TriggerTypeLogon)
            {
                return true;
            }
        }

        return false;
    }

    private static string DescribeTriggers(dynamic task)
    {
        var text = "登录时";
        foreach (dynamic trigger in task.Definition.Triggers)
        {
            if ((int)trigger.Type == TriggerTypeLogon)
            {
                try
                {
                    var userId = (string?)trigger.UserId;
                    if (!string.IsNullOrWhiteSpace(userId))
                    {
                        text = $"登录时（{userId}）";
                    }

                    break;
                }
                catch
                {
                    break;
                }
            }
        }

        return text;
    }

    private static string DescribeActions(dynamic task)
    {
        try
        {
            foreach (dynamic action in task.Definition.Actions)
            {
                if ((int)action.Type != ActionTypeExec)
                {
                    continue;
                }

                var path = (string?)action.Path;
                var arguments = (string?)action.Arguments;
                return string.IsNullOrWhiteSpace(arguments) ? path ?? "" : $"{path} {arguments}";
            }
        }
        catch
        {
            // 动作受保护时仅显示空串
        }

        return string.Empty;
    }

    private static string StateText(int state) => state switch
    {
        1 => "已禁用",
        2 => "排队中",
        3 => "就绪",
        4 => "运行中",
        _ => "未知",
    };
}
