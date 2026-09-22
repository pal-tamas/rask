namespace Rask.Core.Tests.Components;

public partial class ThTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<th></th>", Th.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        // colspan/rowspan/headers now come from the HtmlTableCellElement base; scope/abbr stay on Th.
        // Emit order is unchanged (base attrs first); named arguments keep the call independent of the
        // factory parameter layout.
        Assert.Equal(
            "<th id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" colspan=\"2\" rowspan=\"3\" headers=\"h1\" scope=\"col\" abbr=\"name\"></th>",
            Th
                .Colspan(2)
                .Rowspan(3)
                .Headers("h1")
                .Scope("col")
                .Abbr("name")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<th>&lt;x&gt;</th>", Th["<x>"].ToHtml());
}
