namespace Rask.Core.Tests.Components;

public partial class InsTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<ins></ins>", Ins.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal(
            "<ins id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" cite=\"https://x\" datetime=\"2024-01-01\"></ins>",
            Ins
                .Cite("https://x")
                .DateTime("2024-01-01")
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<ins>&lt;x&gt;</ins>", Ins["<x>"].ToHtml());
}
