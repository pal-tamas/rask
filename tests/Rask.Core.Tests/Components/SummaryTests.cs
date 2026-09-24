namespace Rask.Core.Tests.Components;

public partial class SummaryTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<summary></summary>", Summary.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<summary id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></summary>",
            Summary.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<summary>&lt;x&gt;</summary>", Summary["<x>"].ToHtml());
}
