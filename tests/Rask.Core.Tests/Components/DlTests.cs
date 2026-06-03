namespace Rask.Core.Tests.Components;

public class DlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<dl></dl>", Dl().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<dl id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></dl>",
            Dl("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<dl>&lt;x&gt;</dl>", Dl()["<x>"].ToHtml());
}
