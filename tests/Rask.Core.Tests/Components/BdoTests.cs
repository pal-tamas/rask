namespace Rask.Core.Tests.Components;

public partial class BdoTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<bdo></bdo>", Bdo.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        // `dir` is a GLOBAL attribute now (#693), so it emits with the plain globals — before the
        // data-* group — rather than as a bdo-specific attribute after it.
        Assert.Equal("<bdo id=\"i\" class=\"c\" style=\"s\" dir=\"rtl\" data-k=\"v\"></bdo>",
            Bdo.Dir("rtl").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<bdo>&lt;x&gt;</bdo>", Bdo["<x>"].ToHtml());
}
