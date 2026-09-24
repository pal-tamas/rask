namespace Rask.Core.Tests.Components;

public partial class QTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<q></q>", Q.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<q id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></q>",
            Q.Cite("https://x").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<q>&lt;x&gt;</q>", Q["<x>"].ToHtml());
}
