namespace Rask.Core.Tests.Components;

public partial class H3Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<h3></h3>", H3.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<h3 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h3>",
            H3.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<h3>&lt;x&gt;</h3>", H3["<x>"].ToHtml());
}
