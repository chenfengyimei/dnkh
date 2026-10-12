using System.Text.Json;

namespace Dnkhw.Core.Optimize;

internal sealed record DwordBackup(bool Exists, int Value);

internal sealed record OptimizeBackup(
    Dictionary<string, DwordBackup> RegistryValues,
    string? PowerScheme,
    bool? ServiceRunning);

internal static class OptimizeBackupStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static string StateDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "dnkhw");

    private static string StateFile => Path.Combine(StateDirectory, "optimize-backup.json");

    public static void SaveIfMissing(string actionId, Func<OptimizeBackup> capture)
    {
        lock (Sync)
        {
            var state = ReadState();
            if (state.ContainsKey(actionId))
            {
                return;
            }

            state[actionId] = capture();
            WriteState(state);
        }
    }

    public static bool TryGet(string actionId, out OptimizeBackup backup)
    {
        lock (Sync)
        {
            return ReadState().TryGetValue(actionId, out backup!);
        }
    }

    public static void Remove(string actionId)
    {
        lock (Sync)
        {
            var state = ReadState();
            if (!state.Remove(actionId))
            {
                return;
            }

            if (state.Count == 0)
            {
                File.Delete(StateFile);
            }
            else
            {
                WriteState(state);
            }
        }
    }

    private static Dictionary<string, OptimizeBackup> ReadState()
    {
        if (!File.Exists(StateFile))
        {
            return new Dictionary<string, OptimizeBackup>(StringComparer.Ordinal);
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, OptimizeBackup>>(
                    File.ReadAllText(StateFile), JsonOptions)
                ?? new Dictionary<string, OptimizeBackup>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"优化备份文件损坏，已停止修改以保护原设置：{StateFile}", ex);
        }
    }

    private static void WriteState(Dictionary<string, OptimizeBackup> state)
    {
        Directory.CreateDirectory(StateDirectory);
        var temp = Path.Combine(StateDirectory, $"optimize-backup.{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temp, JsonSerializer.Serialize(state, JsonOptions));
            File.Move(temp, StateFile, overwrite: true);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // 替换成功后临时文件已不存在；失败时不覆盖主错误。
            }
        }
    }
}
