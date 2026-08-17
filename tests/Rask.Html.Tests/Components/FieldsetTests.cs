namespace Rask.Html.Tests.Components;

public partial class FieldsetTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<fieldset></fieldset>", Fieldset.ToHtml());

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes()
    {
        Assert.Equal(
            "<fieldset id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" disabled form=\"f\" name=\"n\"></fieldset>",
            Fieldset
                .Disabled(true)
                .Form("f")
                .Name("n")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<fieldset>&lt;x&gt;</fieldset>", Fieldset["<x>"].ToHtml());
}
