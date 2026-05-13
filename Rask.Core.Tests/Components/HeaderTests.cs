using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HeaderTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<header></header>", Header().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<header id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></header>",
            Header(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<header>&lt;x&gt;</header>", Header()["<x>"].ToHtml());
}
