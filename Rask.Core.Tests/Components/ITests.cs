namespace Rask.Core.Tests.Components;

public class ITests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<i></i>", I().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<i id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></i>",
            I("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<i>&lt;x&gt;</i>", I()["<x>"].ToHtml());
}
