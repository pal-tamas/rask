using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class TheadTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<thead></thead>", Thead().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<thead id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></thead>",
            Thead(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<thead>&lt;x&gt;</thead>", Thead()["<x>"].ToHtml());
}
