namespace Rask.Core.Tests.Components;

public partial class DatalistTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<datalist></datalist>", Datalist.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<datalist id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></datalist>",
            Datalist.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<datalist>&lt;x&gt;</datalist>", Datalist["<x>"].ToHtml());
}
