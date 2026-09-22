namespace Rask.Core.Tests.Components;

public partial class HtmlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() => Assert.Equal("<html></html>", Html.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            // lang/dir are the GLOBAL attributes inherited from Element now (#693) rather than <html>'s
            // own, so they emit with the plain globals — before data-* — leaving xmlns as the only
            // html-specific attribute after it.
            "<html id=\"i\" class=\"c\" style=\"s\" lang=\"en\" dir=\"ltr\" data-k=\"v\" xmlns=\"http://www.w3.org/1999/xhtml\"></html>",
            Html
                .Lang("en")
                .Dir("ltr")
                .Xmlns("http://www.w3.org/1999/xhtml")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<html>&lt;x&gt;</html>", Html["<x>"].ToHtml());
}
