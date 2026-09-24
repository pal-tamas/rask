namespace Rask.Core.Tests.Components;

public partial class H6Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<h6></h6>", H6.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<h6 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h6>",
            H6.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<h6>&lt;x&gt;</h6>", H6["<x>"].ToHtml());
}
