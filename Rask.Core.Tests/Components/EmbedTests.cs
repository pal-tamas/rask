namespace Rask.Core.Tests.Components;

public class EmbedTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<embed />", Embed().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<embed id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/x.swf\" type=\"application/x-shockwave-flash\" width=\"400\" height=\"300\" />",
            Embed("/x.swf", "application/x-shockwave-flash", 400, 300, "i", "c", "s",
                new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
