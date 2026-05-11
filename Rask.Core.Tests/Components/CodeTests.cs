using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class CodeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<code></code>", new Code(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Code.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<code id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></code>",
            new Code(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<code>&lt;x&gt;</code>", new Code(null, "<x>").ToHtml());
}
