using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OutputTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<output></output>", new Output(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Output.Props("x", "f", "n",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<output id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" for=\"x\" form=\"f\" name=\"n\"></output>",
            new Output(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<output>&lt;x&gt;</output>", new Output(null, "<x>").ToHtml());
}
