using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DelTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<del></del>", new Del(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Del.Props("https://x", "2024-01-01",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<del id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\" datetime=\"2024-01-01\"></del>",
            new Del(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<del>&lt;x&gt;</del>", new Del(null, "<x>").ToHtml());
}
