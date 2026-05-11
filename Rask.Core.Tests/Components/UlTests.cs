using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class UlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ul></ul>", new Ul(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Ul.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<ul id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ul>",
            new Ul(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ul>&lt;x&gt;</ul>", new Ul(null, "<x>").ToHtml());
}
