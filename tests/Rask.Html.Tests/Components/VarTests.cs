namespace Rask.Html.Tests.Components;

public partial class VarTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<var></var>", Var.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes() =>
        Assert.Equal(
            "<var id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></var>",
            Var
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<var>&lt;x&gt;</var>", Var["<x>"].ToHtml());
}
