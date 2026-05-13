using Rask.Core.Components;

namespace Rask.Core.Tests.Components;

public class AddressTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<address></address>", new Address().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
                Assert.Equal("<address id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></address>",
            new Address { Id = "i", Class = "c", Style = "s", Data = new Dictionary<string, string?> { ["k"] = "v" } }.ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<address>&lt;x&gt;</address>", new Address { Children = ["<x>"] }.ToHtml());
}
