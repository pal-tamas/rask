using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class OutputTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<output></output>", new Output().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<output id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" for=\"x\" form=\"f\" name=\"n\"></output>",
            new Output { For = "x", Form = "f", Name = "n", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<output>&lt;x&gt;</output>", new Output { Children = ["<x>"] }.ToHtml());
}
