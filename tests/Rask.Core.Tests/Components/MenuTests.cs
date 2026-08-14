namespace Rask.Core.Tests.Components;

public partial class MenuTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<menu></menu>", Menu.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal("<menu id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></menu>",
            Menu.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<menu>&lt;x&gt;</menu>", Menu["<x>"].ToHtml());
}
