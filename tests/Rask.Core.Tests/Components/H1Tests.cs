namespace Rask.Core.Tests.Components;

public partial class H1Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<h1></h1>", H1.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<h1 id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></h1>",
            H1.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<h1>&lt;x&gt;</h1>", H1["<x>"].ToHtml());
}
