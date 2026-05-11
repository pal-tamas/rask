using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TimeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<time></time>", new Time(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Time.Props("2024-01-01", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<time id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" datetime=\"2024-01-01\"></time>",
            new Time(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<time>&lt;x&gt;</time>", new Time(null, "<x>").ToHtml());
}
