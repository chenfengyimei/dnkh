using System.IO;
using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Windows.Media.Imaging;

namespace Dnkhw.Services;

/// <summary>
/// Windows 的 SPI_SETDESKWALLPAPER 仅稳定支持 BMP/JPG。
/// 将 PNG 等界面允许选择的图片转为短路径 BMP，兼容 Windows 10。
/// </summary>
public static class WallpaperImageCompatibility
{
    private static readonly HashSet<string> NativeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".bmp", ".jpg", ".jpeg",
    };

    public static string Prepare(string sourcePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        if (!File.Exists(sourcePath))
        {
            throw new FileNotFoundException("壁纸文件不存在", sourcePath);
        }

        var extension = Path.GetExtension(sourcePath);
        if (NativeExtensions.Contains(extension))
        {
            return sourcePath;
        }

        if (!extension.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            throw new NotSupportedException($"不支持的壁纸格式：{extension}");
        }

        var info = new FileInfo(sourcePath);
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}")))[..16];
        var directory = CreateCompatibleDirectory();

        var target = Path.Combine(directory, $"wallpaper-{fingerprint}.bmp");
        if (File.Exists(target) && new FileInfo(target).Length > 0)
        {
            return target;
        }

        var temp = target + $".{Guid.NewGuid():N}.tmp";
        try
        {
            var decoder = BitmapDecoder.Create(
                new Uri(info.FullName, UriKind.Absolute),
                BitmapCreateOptions.PreservePixelFormat,
                BitmapCacheOption.OnLoad);
            if (decoder.Frames.Count == 0)
            {
                throw new InvalidDataException("图片中没有可用画面");
            }

            var encoder = new BmpBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(decoder.Frames[0]));
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                encoder.Save(stream);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temp, target, overwrite: true);
            LogService.Info($"壁纸开荒：已转换 Windows 兼容图片；源格式={extension}；输出={Path.GetFileName(target)}；大小={new FileInfo(target).Length}");
            return target;
        }
        catch (Exception ex)
        {
            LogService.Error($"壁纸开荒：图片兼容转换失败；源文件={Path.GetFileName(sourcePath)}；格式={extension}", ex);
            throw new InvalidOperationException("图片转换为 Windows 兼容格式失败", ex);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // 转换失败后的临时文件由系统临时清理兜底。
            }
        }
    }

    private static string CreateCompatibleDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            string.IsNullOrWhiteSpace(localAppData)
                ? null
                : Path.Combine(localAppData, "dnkhw", "wallpaper-compatible"),
            Path.Combine(Path.GetTempPath(), "dnkhw", "wallpaper-compatible"),
        };
        var failures = new List<Exception>();

        foreach (var candidate in candidates.Where(static path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate!);
                Directory.CreateDirectory(fullPath);
                if (failures.Count > 0 || string.IsNullOrWhiteSpace(localAppData))
                {
                    LogService.Warn($"壁纸开荒：兼容图片缓存已回退到临时目录；目录={fullPath}；首个错误={failures.FirstOrDefault()?.Message ?? "LocalApplicationData 为空"}");
                }

                return fullPath;
            }
            catch (Exception ex) when (ex is
                ArgumentException or
                NotSupportedException or
                PathTooLongException or
                UnauthorizedAccessException or
                IOException or
                SecurityException)
            {
                failures.Add(ex);
            }
        }

        throw new InvalidOperationException(
            "无法创建壁纸兼容图片缓存目录",
            new AggregateException(failures));
    }
}
