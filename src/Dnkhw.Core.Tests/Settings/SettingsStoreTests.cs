using Dnkhw.Core.Settings;

namespace Dnkhw.Core.Tests.Settings;

public class SettingsStoreTests
{
    private static string TempJsonPath()
        => Path.Combine(Path.GetTempPath(), $"dnkhw-test-{Guid.NewGuid():N}.json");

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var settings = SettingsStore.Load(TempJsonPath());

        Assert.Equal(AppTheme.Follow, settings.Theme);
        Assert.True(settings.RememberLastPage);
        Assert.Null(settings.LastPageKey);
        Assert.Null(settings.AppInstallRoot);
    }

    [Fact]
    public void SaveThenLoad_RoundTripsAllFields()
    {
        var path = TempJsonPath();
        try
        {
            var settings = new AppSettings
            {
                Theme = AppTheme.Dark,
                RememberLastPage = false,
                LastPageKey = "wallpaper",
                AppInstallRoot = Path.Combine(Path.GetTempPath(), "dnkhw-apps"),
            };
            SettingsStore.Save(settings, path);
            var loaded = SettingsStore.Load(path);

            Assert.Equal(AppTheme.Dark, loaded.Theme);
            Assert.False(loaded.RememberLastPage);
            Assert.Equal("wallpaper", loaded.LastPageKey);
            Assert.Equal(settings.AppInstallRoot, loaded.AppInstallRoot);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_CorruptFile_FallsBackToDefaults()
    {
        var path = TempJsonPath();
        try
        {
            File.WriteAllText(path, "{ 这不是合法 JSON ");
            var settings = SettingsStore.Load(path);

            Assert.Equal(AppTheme.Follow, settings.Theme);
            Assert.True(settings.RememberLastPage);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_NewFieldInFile_IsIgnoredAndDefaultsKept()
    {
        var path = TempJsonPath();
        try
        {
            File.WriteAllText(path, """{ "Theme": 2, "FutureField": "x" }""");
            var settings = SettingsStore.Load(path);

            Assert.Equal(AppTheme.Dark, settings.Theme);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Save_LeavesNoTempFile()
    {
        var path = TempJsonPath();
        try
        {
            SettingsStore.Save(new AppSettings { LastPageKey = "check" }, path);

            Assert.Empty(Directory.EnumerateFiles(
                Path.GetDirectoryName(path)!,
                Path.GetFileName(path) + ".*.tmp"));
            Assert.True(File.Exists(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_InvalidThemeAndUnsafePageKey_AreNormalized()
    {
        var path = TempJsonPath();
        try
        {
            File.WriteAllText(path, """{ "Theme": 999, "LastPageKey": "\u0001bad" }""");

            var settings = SettingsStore.Load(path);

            Assert.Equal(AppTheme.Follow, settings.Theme);
            Assert.Null(settings.LastPageKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_RelativeAppInstallRoot_IsDiscarded()
    {
        var path = TempJsonPath();
        try
        {
            File.WriteAllText(path, """{ "AppInstallRoot": "relative\\apps" }""");

            var settings = SettingsStore.Load(path);

            Assert.Null(settings.AppInstallRoot);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
