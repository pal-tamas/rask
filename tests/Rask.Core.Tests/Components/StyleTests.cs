namespace Rask.Core.Tests.Components;

public partial class StyleTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() => Assert.Equal("<style></style>", Style.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        // `title` is the ordinary global attribute (on <style> it names an alternative stylesheet), so it
        // is inherited from Element and renders in the global group after style — not among <style>'s own
        // type/media/nonce, where it used to sit as a redeclared property.
        Assert.Equal(
            "<style id=\"i\" class=\"c\" style=\"s\" title=\"main\" data-k=\"v\" type=\"text/css\" media=\"all\" nonce=\"abc\"></style>",
            Style
                .Type("text/css")
                .Media("all")
                .Nonce("abc")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" })
                .Title("main")
                .ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<style>&lt;x&gt;</style>", Style["<x>"].ToHtml());
}
