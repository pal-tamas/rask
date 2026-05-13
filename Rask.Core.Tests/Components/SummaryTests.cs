using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class SummaryTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<summary></summary>", Summary().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<summary id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></summary>",
            Summary(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<summary>&lt;x&gt;</summary>", Summary()["<x>"].ToHtml());
}
