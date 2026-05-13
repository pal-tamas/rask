using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BodyTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<body></body>", Body().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        
        Assert.Equal(
            "<body id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></body>",
            Body(Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<body>&lt;x&gt;</body>", Body()["<x>"].ToHtml());
}
