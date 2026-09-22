namespace Rask.Core.Tests.Components;

public partial class ColgroupTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<colgroup></colgroup>", Colgroup.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<colgroup id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" span=\"3\"></colgroup>",
            Colgroup.Span(3).Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<colgroup>&lt;x&gt;</colgroup>", Colgroup["<x>"].ToHtml());
}
