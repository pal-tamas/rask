namespace Rask.Core.Tests.Components;

public class OlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ol></ol>", Ol().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<ol id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"1\" reversed start=\"5\"></ol>",
            Ol("1", true, 5, "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ol>&lt;x&gt;</ol>", Ol()["<x>"].ToHtml());
}
