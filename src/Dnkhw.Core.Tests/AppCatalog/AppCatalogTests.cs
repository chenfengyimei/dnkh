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
}
