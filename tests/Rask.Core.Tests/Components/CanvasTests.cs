namespace Rask.Core.Tests.Components;

public partial class CanvasTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<canvas></canvas>", Canvas.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<canvas id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" width=\"300\" height=\"150\"></canvas>",
            Canvas
                .Width(300)
                .Height(150)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<canvas>&lt;x&gt;</canvas>", Canvas["<x>"].ToHtml());
}
