using Dnkhw.Core.AppCatalog;

namespace Dnkhw.Core.Tests.AppCatalog;

public class AppCatalogTests
{
    [Fact]
    public void LoadDefault_ParsesEmbeddedCatalog()
    {
        var data = AppCatalogLoader.LoadDefault();

        Assert.True(data.Categories.Count >= 7);
        Assert.All(data.Categories, c =>
        {
            Assert.False(string.IsNullOrWhiteSpace(c.Id));
            Assert.False(string.IsNullOrWhiteSpace(c.Name));
            Assert.NotEmpty(c.Apps);
        });

        var total = data.Categories.Sum(c => c.Apps.Count);
        Assert.True(total >= 20, $"清单应用数量过少：{total}");
        Assert.All(data.Categories.SelectMany(c => c.Apps), a =>
        {
            Assert.False(string.IsNullOrWhiteSpace(a.Name));
            Assert.StartsWith("https://", a.Homepage, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void Parse_RejectsDuplicateAppName()
    {
        const string json = """
        {
          "categories": [
            { "id": "a", "name": "A", "apps": [
              { "name": "重复", "description": "x", "homepage": "https://example.com", "wingetId": "X.Y" },
              { "name": "重复", "description": "x", "homepage": "https://example.com", "wingetId": "Z.W" }
            ]}
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => AppCatalogLoader.Parse(json));
    }

    [Fact]
    public void Parse_RejectsBadHomepage()
    {
        const string json = """
        {
          "categories": [
            { "id": "a", "name": "A", "apps": [
              { "name": "App", "description": "x", "homepage": "ftp://not-https.com", "wingetId": "X.Y" }
            ]}
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => AppCatalogLoader.Parse(json));
    }

    [Fact]
    public void Parse_RejectsDuplicateWingetId()
    {
        const string json = """
        {
          "categories": [
            { "id": "a", "name": "A", "apps": [
              { "name": "App1", "description": "x", "homepage": "https://example.com", "wingetId": "X.Y" },
              { "name": "App2", "description": "x", "homepage": "https://example.com", "wingetId": "x.y" }
            ]}
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => AppCatalogLoader.Parse(json));
    }

    [Fact]
    public void Parse_RejectsWingetIdContainingArguments()
    {
        const string json = """
        {
          "categories": [
            { "id": "a", "name": "A", "apps": [
              { "name": "App", "description": "x", "homepage": "https://example.com", "wingetId": "Safe.Id --source untrusted" }
            ]}
          ]
        }
        """;

        Assert.Throws<InvalidOperationException>(() => AppCatalogLoader.Parse(json));
    }

    [Fact]
    public void BuildInstallArguments_CustomRoot_UsesDedicatedPackageDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "dnkhw apps");

        var arguments = WingetInstaller.BuildInstallArguments("Vendor.Product", root, interactive: false);

        var locationIndex = arguments.ToList().IndexOf("--location");
        Assert.True(locationIndex >= 0);
        Assert.Equal(Path.Combine(root, "Vendor.Product"), arguments[locationIndex + 1]);
        Assert.Contains("--silent", arguments);
        Assert.Contains("--disable-interactivity", arguments);
        Assert.DoesNotContain("--interactive", arguments);
    }

    [Fact]
    public void BuildInstallArguments_Interactive_DoesNotDisableInstallerUi()
    {
        var arguments = WingetInstaller.BuildInstallArguments(
            "Vendor.Product",
            Path.GetTempPath(),
            interactive: true);

        Assert.Contains("--interactive", arguments);
        Assert.DoesNotContain("--silent", arguments);
        Assert.DoesNotContain("--disable-interactivity", arguments);
    }

    [Fact]
    public void NormalizeInstallRoot_RejectsRelativePath()
    {
        Assert.Throws<ArgumentException>(() => WingetInstaller.NormalizeInstallRoot("apps"));
    }
}
