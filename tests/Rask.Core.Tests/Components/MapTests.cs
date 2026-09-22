namespace Rask.Core.Tests.Components;

public partial class MapTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<map></map>", Map.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<map id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"m\"></map>",
            Map.Name("m").Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<map>&lt;x&gt;</map>", Map["<x>"].ToHtml());
}
