namespace Rask.Core.Tests.Components;

public partial class TdTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<td></td>", Td.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<td id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" colspan=\"2\" rowspan=\"3\" headers=\"h1 h2\"></td>",
            Td
                .Colspan(2)
                .Rowspan(3)
                .Headers("h1 h2")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<td>&lt;x&gt;</td>", Td["<x>"].ToHtml());
}
