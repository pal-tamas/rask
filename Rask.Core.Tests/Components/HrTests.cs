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
                Assert.Equal("<hr id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" />",
            new Hr { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }
}
