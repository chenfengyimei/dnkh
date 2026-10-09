namespace Dnkhw.Core.Downloads;

public sealed record DownloadDrive(string Root, string Label, long FreeBytes);

/// <summary>下载路径换盘模块的整体状态。</summary>
public sealed record DownloadsStatus(
    string CurrentPath,
    string DefaultPath,
    bool IsRedirected,
    long SourceFileCount,
    long SourceTotalBytes,
    IReadOnlyList<DownloadDrive> Drives,
    IReadOnlyList<BrowserDownloadInfo> Browsers,
    string? SavedPreviousPath);

public sealed record FileFailure(string RelativePath, string Reason);

public sealed record MigrationProgress(int Completed, int Total, string CurrentFile, int FailedCount);

public sealed record MigrationResult(
    string OldPath,
    string NewPath,
    int MovedCount,
    bool MoveFiles,
    IReadOnlyList<FileFailure> Failures);
