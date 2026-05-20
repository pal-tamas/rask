namespace Rask.Core.Tests.Components;

public class ColgroupTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<colgroup></colgroup>", Colgroup().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<colgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\"></colgroup>",
            Colgroup(3, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<colgroup>&lt;x&gt;</colgroup>", Colgroup()["<x>"].ToHtml());
}
