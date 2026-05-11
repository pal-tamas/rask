using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class BodyTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() => Assert.Equal("<body></body>", new Body(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Body.Props(
            "i",
            "c",
            "s",
            new Dictionary<string, string?> { ["k"] = "v" });

        Assert.Equal(
            "<body id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></body>",
            new Body(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<body>&lt;x&gt;</body>", new Body(null, "<x>").ToHtml());
}
