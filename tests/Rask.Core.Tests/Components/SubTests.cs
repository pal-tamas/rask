namespace Rask.Core.Tests.Components;

public partial class SubTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<sub></sub>", Sub.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<sub id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></sub>",
            Sub.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<sub>&lt;x&gt;</sub>", Sub["<x>"].ToHtml());
}
