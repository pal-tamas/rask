namespace Rask.Core.Tests.Components;

public partial class OlTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<ol></ol>", Ol.ToHtml());

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes()
    {
        Assert.Equal("<ol id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" type=\"1\" reversed start=\"5\"></ol>",
            Ol
                .Type("1")
                .Reversed(true)
                .Start(5)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());
    }

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<ol>&lt;x&gt;</ol>", Ol["<x>"].ToHtml());
}
