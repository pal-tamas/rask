namespace Rask.Core.Tests.Components;

public partial class SectionTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<section></section>", Section.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<section id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></section>",
            Section.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<section>&lt;x&gt;</section>", Section["<x>"].ToHtml());
}
