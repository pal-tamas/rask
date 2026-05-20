namespace Rask.Core.Tests.Components;

public class UlTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ul></ul>", Ul().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<ul id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></ul>",
            Ul("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ul>&lt;x&gt;</ul>", Ul()["<x>"].ToHtml());
}
