namespace Rask.Core.Tests.Components;

public class AddressTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<address></address>", Address().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<address id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></address>",
            Address("i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<address>&lt;x&gt;</address>", Address()["<x>"].ToHtml());
}
