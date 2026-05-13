using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BdoTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<bdo></bdo>", Bdo().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<bdo id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" dir=\"rtl\"></bdo>",
            Bdo(Dir: "rtl", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<bdo>&lt;x&gt;</bdo>", Bdo()["<x>"].ToHtml());
}
