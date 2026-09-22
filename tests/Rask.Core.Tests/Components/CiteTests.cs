namespace Rask.Core.Tests.Components;

public partial class CiteTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<cite></cite>", Cite.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<cite id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></cite>",
            Cite.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<cite>&lt;x&gt;</cite>", Cite["<x>"].ToHtml());
}
