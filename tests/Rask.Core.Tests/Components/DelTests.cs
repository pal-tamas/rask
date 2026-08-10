namespace Rask.Core.Tests.Components;

public partial class DelTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<del></del>", Del.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<del id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\" datetime=\"2024-01-01\"></del>",
            Del
                .Cite("https://x")
                .DateTime("2024-01-01")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<del>&lt;x&gt;</del>", Del["<x>"].ToHtml());
}
