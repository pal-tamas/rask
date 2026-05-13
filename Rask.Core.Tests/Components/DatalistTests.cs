using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class DatalistTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<datalist></datalist>", new Datalist().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<datalist id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></datalist>",
            new Datalist { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<datalist>&lt;x&gt;</datalist>", new Datalist { Children = ["<x>"] }.ToHtml());
}
