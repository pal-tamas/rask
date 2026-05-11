using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class HrTests
{
    [Fact]
    public void Render_NullProps_ReturnsSelfClosingTag() =>
        Assert.Equal("<hr />", new Hr().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Hr.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<hr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            new Hr(props).ToHtml());
    }
}
