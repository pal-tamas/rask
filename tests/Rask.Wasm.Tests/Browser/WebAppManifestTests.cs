using System.Text.Json;
using Rask.Wasm.Browser;

namespace Rask.Wasm.Tests.Browser;

public class WebAppManifestTests
{
    [Fact]
    public void ToJson_UsesManifestSnakeCaseKeys()
    {
        var json = new WebAppManifest
        {
            Name = "My App",
            ShortName = "App",
            ThemeColor = "#512BD4",
            BackgroundColor = "#fff"
        }.ToJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.Equal("My App", root.GetProperty("name").GetString());
        Assert.Equal("App", root.GetProperty("short_name").GetString());
        Assert.Equal("#512BD4", root.GetProperty("theme_color").GetString());
        Assert.Equal("#fff", root.GetProperty("background_color").GetString());
        // Defaults present.
        Assert.Equal(".", root.GetProperty("start_url").GetString());
        Assert.Equal(".", root.GetProperty("scope").GetString());
    }

    [Fact]
    public void ToJson_OmitsUnsetOptionalMembers()
    {
        var json = new WebAppManifest { Name = "Bare" }.ToJson();

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.False(root.TryGetProperty("short_name", out _));
        Assert.False(root.TryGetProperty("description", out _));
        Assert.False(root.TryGetProperty("theme_color", out _));
        Assert.False(root.TryGetProperty("background_color", out _));
    }

    [Theory]
    [InlineData(DisplayMode.Standalone, "standalone")]
    [InlineData(DisplayMode.Fullscreen, "fullscreen")]
    [InlineData(DisplayMode.MinimalUi, "minimal-ui")]
    [InlineData(DisplayMode.Browser, "browser")]
    public void ToJson_SerializesDisplayAsSpecString(DisplayMode mode, string expected)
    {
        var json = new WebAppManifest { Name = "X", Display = mode }.ToJson();

        using var doc = JsonDocument.Parse(json);
        Assert.Equal(expected, doc.RootElement.GetProperty("display").GetString());
    }

    [Fact]
    public void ToJson_SerializesIcons_WithPurposeOmittedWhenNull()
    {
        var json = new WebAppManifest
        {
            Name = "X",
            Icons =
            [
                new ManifestIcon("icon.svg", "any", "image/svg+xml", "any maskable"),
                new ManifestIcon("icon-192.png", "192x192", "image/png")
            ]
        }.ToJson();

        using var doc = JsonDocument.Parse(json);
        var icons = doc.RootElement.GetProperty("icons");
        Assert.Equal(2, icons.GetArrayLength());
        Assert.Equal("icon.svg", icons[0].GetProperty("src").GetString());
        Assert.Equal("any maskable", icons[0].GetProperty("purpose").GetString());
        Assert.False(icons[1].TryGetProperty("purpose", out _));
    }
}
