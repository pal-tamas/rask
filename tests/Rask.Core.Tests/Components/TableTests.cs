namespace Rask.Core.Tests.Components;

public partial class TableTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<table></table>", Table.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<table id=\"i\" class=\"c\" style=\"s\" data-k=\"v\"></table>",
            Table.Id("i").Class("c").Style("s").Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<table>&lt;x&gt;</table>", Table["<x>"].ToHtml());
}
