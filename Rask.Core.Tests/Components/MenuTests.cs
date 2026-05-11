using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MenuTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<menu></menu>", new Menu(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Menu.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<menu id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></menu>",
            new Menu(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<menu>&lt;x&gt;</menu>", new Menu(null, "<x>").ToHtml());
}
