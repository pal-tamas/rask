using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DatalistTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<datalist></datalist>", new Datalist(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Datalist.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<datalist id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></datalist>",
            new Datalist(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<datalist>&lt;x&gt;</datalist>", new Datalist(null, "<x>").ToHtml());
}
