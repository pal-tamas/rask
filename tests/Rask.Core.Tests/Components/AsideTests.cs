namespace Rask.Core.Tests.Components;

public partial class AsideTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<aside></aside>", Aside.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<aside id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></aside>",
            Aside.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<aside>&lt;x&gt;</aside>", Aside["<x>"].ToHtml());
}
