namespace Rask.Core.Tests.Components;

public class AsideTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<aside></aside>", Aside().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<aside id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></aside>",
            Aside("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<aside>&lt;x&gt;</aside>", Aside()["<x>"].ToHtml());
}
