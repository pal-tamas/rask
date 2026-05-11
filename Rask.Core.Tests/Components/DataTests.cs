using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DataTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<data></data>", new Data(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Data.Props("42", "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<data id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"42\"></data>",
            new Data(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<data>&lt;x&gt;</data>", new Data(null, "<x>").ToHtml());
}
