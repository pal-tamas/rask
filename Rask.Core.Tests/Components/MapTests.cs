using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MapTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<map></map>", Map().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<map id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"m\"></map>",
            Map(Name: "m", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<map>&lt;x&gt;</map>", Map()["<x>"].ToHtml());
}
