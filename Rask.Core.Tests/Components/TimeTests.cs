using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TimeTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<time></time>", new Time().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<time id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" datetime=\"2024-01-01\"></time>",
            new Time { DateTime = "2024-01-01", Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<time>&lt;x&gt;</time>", new Time { Children = ["<x>"] }.ToHtml());
}
