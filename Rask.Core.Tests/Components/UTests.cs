using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class UTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<u></u>", new U(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new U.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<u id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></u>",
            new U(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<u>&lt;x&gt;</u>", new U(null, "<x>").ToHtml());
}
