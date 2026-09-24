namespace Rask.Core.Tests.Components;

public partial class DelTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<del></del>", Del.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<del id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\" datetime=\"2024-01-01\"></del>",
            Del
                .Cite("https://x")
                .DateTime("2024-01-01")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_text_child_is_html_encoded() =>
        Assert.Equal("<del>&lt;x&gt;</del>", Del["<x>"].ToHtml());
}
