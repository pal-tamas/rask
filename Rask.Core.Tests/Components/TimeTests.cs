namespace Rask.Core.Tests.Components;

public class TimeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<time></time>", Time().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<time id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" datetime=\"2024-01-01\"></time>",
            Time("2024-01-01", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<time>&lt;x&gt;</time>", Time()["<x>"].ToHtml());
}
