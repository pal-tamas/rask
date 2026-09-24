namespace Rask.Core.Tests.Components;

public partial class OptionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<option></option>", Option.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<option id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"v\" selected disabled label=\"L\"></option>",
            Option
                .Value("v")
                .Selected(true)
                .Disabled(true)
                .Label("L")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<option>&lt;x&gt;</option>", Option["<x>"].ToHtml());
}
