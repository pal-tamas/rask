namespace Rask.Core.Tests.Components;

public partial class H5Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<h5></h5>", H5.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<h5 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h5>",
            H5.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<h5>&lt;x&gt;</h5>", H5["<x>"].ToHtml());
}
