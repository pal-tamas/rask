using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class MeterTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<meter></meter>", Meter().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal(
            "<meter id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"7\" min=\"0\" max=\"10\" low=\"2\" high=\"8\" optimum=\"5\" form=\"f\"></meter>",
            Meter(Value: 7, Min: 0, Max: 10, Low: 2, High: 8, Optimum: 5, Form: "f", Id: "i", Class: "c", Style: "s", Data: new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<meter>&lt;x&gt;</meter>", Meter()["<x>"].ToHtml());
}
