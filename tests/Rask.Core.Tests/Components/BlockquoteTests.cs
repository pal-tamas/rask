namespace Rask.Core.Tests.Components;

public partial class BlockquoteTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<blockquote></blockquote>", Blockquote.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<blockquote id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\"></blockquote>",
            Blockquote
                .Cite("https://x")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<blockquote>&lt;x&gt;</blockquote>", Blockquote["<x>"].ToHtml());
}
