namespace Rask.Core.Tests.Components;

public class DtTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dt></dt>", Dt().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<dt id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dt>",
            Dt("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dt>&lt;x&gt;</dt>", Dt()["<x>"].ToHtml());
}
