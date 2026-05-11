using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AddressTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<address></address>", new Address(null).ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        var props = new Address.Props("i", "c", "s",
            new Dictionary<string, string?> { ["k"] = "v" });
        Assert.Equal("<address id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></address>",
            new Address(props).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<address>&lt;x&gt;</address>", new Address(null, "<x>").ToHtml());
}
