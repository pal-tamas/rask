// Template — copy to tests/Rask.Html.Tests/Components/{Tag}Tests.cs.
// Asserts exact attribute order: id, class, style, data-*, then tag-specific.
//
// `partial` and `: RaskMarkup` are both load-bearing. The tag family ships from Rask.Html, so its
// builder entries are INJECTED into this project's own markup hosts rather than inherited from
// Rask.Core — a non-partial host gets none of them (RASK036) and `{Tag}` would not resolve.
namespace Rask.Html.Tests.Components;

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
