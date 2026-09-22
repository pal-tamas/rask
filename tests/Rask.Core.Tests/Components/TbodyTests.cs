namespace Rask.Core.Tests.Components;

public partial class TbodyTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<tbody></tbody>", Tbody.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<tbody id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></tbody>",
            Tbody.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<tbody>&lt;x&gt;</tbody>", Tbody["<x>"].ToHtml());
}
