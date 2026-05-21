using Rask.Core;
using Rask.Example.Shared.Demos;
using Rask.Example.Shared.Tests.Infrastructure;
using static Rask.Example.Shared.Demos.Components;

namespace Rask.Example.Shared.Tests.Demos;

public sealed class ScopedDemoTests
{
    [Fact]
    public void ScopedRed_Render_EmitsBoxClass_AndDotChild()
    {
        var html = new LiveHost(() => ScopedRed(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("box", html);
        Assert.Contains("dot", html);
        Assert.Contains("should be red", html);
    }

    [Fact]
    public void ScopedBlue_Render_EmitsBoxClass_AndDotChild()
    {
        var html = new LiveHost(() => ScopedBlue(), TestServices.Default()).RenderAsLiveRoot();
        Assert.Contains("box", html);
        Assert.Contains("dot", html);
        Assert.Contains("should be blue", html);
    }

    [Fact]
    public void ScopedRed_AndBlue_GetDistinct_DataScopeAttributes()
    {
        var redHtml = new LiveHost(() => ScopedRed(), TestServices.Default()).RenderAsLiveRoot();
        var blueHtml = new LiveHost(() => ScopedBlue(), TestServices.Default()).RenderAsLiveRoot();

        var redScope = ExtractDataScope(redHtml);
        var blueScope = ExtractDataScope(blueHtml);

        Assert.NotNull(redScope);
        Assert.NotNull(blueScope);
        Assert.NotEqual(redScope, blueScope);
    }

    // Reads the data-r-XXXXXXXX attribute the HtmlSerializer stamps on scoped elements.
    private static string? ExtractDataScope(string html)
    {
        const string prefix = "data-r-";
        var idx = html.IndexOf(prefix, StringComparison.Ordinal);
        if (idx < 0)
        {
            return null;
        }

        var end = idx + prefix.Length;
        while (end < html.Length && IsHexOrUnderscore(html[end]))
        {
            end++;
        }

        return html.Substring(idx, end - idx);
    }

    private static bool IsHexOrUnderscore(char c) =>
        (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
}
