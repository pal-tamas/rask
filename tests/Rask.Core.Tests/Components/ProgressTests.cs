namespace Rask.Core.Tests.Components;

public partial class ProgressTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<progress></progress>", Progress.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<progress id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" value=\"50.5\" max=\"100\"></progress>",
            Progress
                .Value(50.5)
                .Max(100)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<progress>&lt;x&gt;</progress>", Progress["<x>"].ToHtml());
}
