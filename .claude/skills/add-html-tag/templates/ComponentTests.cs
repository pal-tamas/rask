// Template — copy to tests/Rask.Core.Tests/Components/{Tag}Tests.cs.
// Asserts exact attribute order: id, class, style, data-*, then tag-specific.
//
// `: RaskMarkup` is load-bearing: a test class is not a Component, so it reaches the builder entries by
// deriving from the markup half of Component. `partial` matters for the entries this project's OWN
// components contribute, which are injected rather than inherited (RASK036).
namespace Rask.Core.Tests.Components;

public partial class {Tag}Tests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void Unset_props_render_only_the_open_and_close_tags() =>
        Assert.Equal("<{tag}></{tag}>", {Tag}.ToHtml());          // self-closing: "<{tag} />"

    [Fact]
    public void Setting_every_prop_emits_the_expected_attributes() =>
        Assert.Equal(
            "<{tag} id=\"i\" class=\"c\" style=\"s\" data-k=\"v\" name=\"n\" open></{tag}>",
            {Tag}
                .Name("n")
                .Open(true)
                .Id("i")
                .Class("c")
                .Style("s")
                .Data(new Dictionary<string, string?> { ["k"] = "v" }).ToHtml());

    [Fact]
    public void A_string_child_is_encoded_as_text() =>
        Assert.Equal("<{tag}>&lt;x&gt;</{tag}>", {Tag}["<x>"].ToHtml());
}
