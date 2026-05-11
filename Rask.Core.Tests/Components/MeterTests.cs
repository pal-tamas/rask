using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MeterTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<meter></meter>", new Meter(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Meter.Props(
            7, 0, 10, 2, 8, 5, "f",
            "i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal(
            "<meter id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"7\" min=\"0\" max=\"10\" low=\"2\" high=\"8\" optimum=\"5\" form=\"f\"></meter>",
            new Meter(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<meter>&lt;x&gt;</meter>", new Meter(null, "<x>").ToHtml());
}
