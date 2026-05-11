using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class EmTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<em></em>", new Em(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Em.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<em id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></em>",
            new Em(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<em>&lt;x&gt;</em>", new Em(null, "<x>").ToHtml());
}
