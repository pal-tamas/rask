namespace Rask.Core.Tests.Components;

public class MenuTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<menu></menu>", Menu().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<menu id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></menu>",
            Menu("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<menu>&lt;x&gt;</menu>", Menu()["<x>"].ToHtml());
}
