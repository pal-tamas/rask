namespace Rask.Core.Tests.Components;

public partial class TimeTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<time></time>", Time.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<time id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" datetime=\"2024-01-01\"></time>",
            Time
                .DateTime("2024-01-01")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<time>&lt;x&gt;</time>", Time["<x>"].ToHtml());
}
