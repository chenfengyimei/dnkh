using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace Dnkhw.Core.Downloads;

/// <summary>
/// 可迁移的已知文件夹定义（桌面/文档/下载/图片/音乐/视频）。
/// PrimaryValueName = Explorer 实际使用的 User Shell Folders 值名（现代 GUID 优先，桌面为旧名）；
/// FallbackValueName = 部分文件夹的旧版名称值（如 Personal/My Pictures）。
/// </summary>
public sealed record KnownFolderDef(
    string Key,
    string Name,
    Guid Id,
    string DefaultSubFolder,
    string PrimaryValueName,
    string? FallbackValueName)
{
    /// <summary>未被重定向时的默认位置（%USERPROFILE% 下同名目录）。</summary>
    public string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        DefaultSubFolder);
}

/// <summary>单个已知文件夹的当前状态。</summary>
public sealed record KnownFolderStatus(
    KnownFolderDef Def,
    string CurrentPath,
    string DefaultPath,
    bool IsRedirected,
    long FileCount,
    long TotalBytes,
    string? SavedPreviousPath,
    bool IsManagedByOneDrive);

public sealed record DownloadDrive(string Root, string Label, long FreeBytes);

public sealed record FileFailure(string RelativePath, string Reason);

public sealed record MigrationProgress(int Completed, int Total, string CurrentFile, int FailedCount);

public sealed record MigrationResult(
    string OldPath,
    string NewPath,
    int MovedCount,
    bool MoveFiles,
    IReadOnlyList<FileFailure> Failures);
