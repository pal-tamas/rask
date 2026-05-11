using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class RubyTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ruby></ruby>", new Ruby(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Ruby.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<ruby id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ruby>",
            new Ruby(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ruby>&lt;x&gt;</ruby>", new Ruby(null, "<x>").ToHtml());
}
