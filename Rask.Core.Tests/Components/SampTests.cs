using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SampTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<samp></samp>", new Samp(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Samp.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<samp id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></samp>",
            new Samp(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<samp>&lt;x&gt;</samp>", new Samp(null, "<x>").ToHtml());
}
