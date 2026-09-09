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
    public void Render_NullProps_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<{tag}></{tag}>", {Tag}.ToHtml());          // self-closing: "<{tag} />"

    [Fact]
    public void Render_AllPropsSet_EmitsExpectedAttributes() =>
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
    public void Render_StringChild_EncodesText() =>
        Assert.Equal("<{tag}>&lt;x&gt;</{tag}>", {Tag}["<x>"].ToHtml());
}
