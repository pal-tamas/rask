namespace Rask.Core.Tests.Components;

public class InsTests
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<ins></ins>", Ins().ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<ins id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\" datetime=\"2024-01-01\"></ins>",
            Ins("https://x", "2024-01-01", "i", "c", "s", new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<ins>&lt;x&gt;</ins>", Ins()["<x>"].ToHtml());
}
