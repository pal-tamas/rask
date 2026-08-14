namespace Rask.Core.Tests.Components;

public partial class EmbedTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<embed />", Embed.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<embed id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" src=\"/x.swf\" type=\"application/x-shockwave-flash\" width=\"400\" height=\"300\" />",
            Embed
                .Src("/x.swf")
                .Type("application/x-shockwave-flash")
                .Width(400)
                .Height(300)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }
}
