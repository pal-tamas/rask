using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class H4Tests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<h4></h4>", H4().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<h4 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h4>",
            H4(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<h4>&lt;x&gt;</h4>", H4()["<x>"].ToHtml());
}
