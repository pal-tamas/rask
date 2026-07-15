#pragma warning disable RASK014 // test-defined components constructed directly

namespace Rask.Testing.Tests;

// HandlerId/Attr reach the first match only, which is useless for a component that wires many elements to
// the same event (a grid's sort headers, a list's row buttons). These pin the all-matches API.
public class MarkupTests
{
    private sealed class Trio : Component
    {
        public List<string> Clicked { get; } = [];

        protected override Component? Render() =>
            Div()[
                Button(Type: "button", OnClick: () => Clicked.Add("a"))["a"],
                Button(Type: "button", OnClick: () => Clicked.Add("b"))["b"],
                Button(Type: "button", OnClick: () => Clicked.Add("c"))["c"]
            ];
    }

    [Fact]
    public void HandlerIds_ReturnsEveryWiredElement()
    {
        var page = RaskTest.Render(new Trio());

        Assert.Equal(3, page.HandlerIds("click").Count);
        Assert.Equal(page.HandlerId("click"), page.HandlerIds("click")[0]);
        Assert.Distinct(page.HandlerIds("click"));
    }

    [Fact]
    public async Task HandlerIds_AreInDocumentOrder_SoAnIndexHitsThatElement()
    {
        // The contract that matters: index N in the list drives the Nth wired element in the markup, not
        // merely *some* element. Without it the list would be unusable for targeting.
        var trio = new Trio();
        var page = RaskTest.Render(trio);

        await page.InvokeAsync(page.HandlerIds("click")[1]);
        Assert.Equal(["b"], trio.Clicked);

        await page.InvokeAsync(page.HandlerIds("click")[2]);
        Assert.Equal(["b", "c"], trio.Clicked);
    }

    [Fact]
    public void HandlerIds_NoneWired_IsEmpty()
    {
        var page = RaskTest.Render(Div()["nothing to click"]);

        Assert.Empty(page.HandlerIds("click"));
    }

    private sealed class TwoLabelled : Component
    {
        protected override Component? Render() =>
            Div()[
                Button(Type: "button", Aria: new Dictionary<string, string?> { ["label"] = "Close" })["x"],
                Button(Type: "button", Aria: new Dictionary<string, string?> { ["label"] = "Open" })["o"]
            ];
    }

    [Fact]
    public void Attrs_ReturnsEveryValue_AndRespectsAttributeBoundaries()
    {
        var page = RaskTest.Render(new TwoLabelled());

        Assert.Equal(["Close", "Open"], page.Attrs("aria-label"));

        // The bare name must not match inside the longer one — for every match, not just the first.
        Assert.Empty(page.Attrs("label"));
    }

    // Markup works over any HTML string, not just a RenderedComponent — e.g. markup lifted out of a live
    // payload, which is why it is public rather than folded into RenderedComponent.
    private const string Payload =
        "<div data-rask-on-click=\"h0\"><span id=\"a\" data-rask-on-click=\"h1\">x</span></div>";

    [Fact]
    public void Markup_ReadsAttributesOutOfARawHtmlString()
    {
        Assert.Equal("h0", Markup.Attr(Payload, "data-rask-on-click"));
        Assert.Equal(["h0", "h1"], Markup.Attrs(Payload, "data-rask-on-click"));
        Assert.Equal("a", Markup.Attr(Payload, "id"));
        Assert.Null(Markup.Attr(Payload, "class"));
        Assert.Empty(Markup.Attrs(Payload, "class"));
    }

    [Fact]
    public void Markup_UnterminatedValue_YieldsNothingRatherThanRunningOn()
    {
        Assert.Null(Markup.Attr("<div id=\"unclosed", "id"));
        Assert.Empty(Markup.Attrs("<div id=\"unclosed", "id"));
    }

    [Fact]
    public void Markup_NullArguments_Throw()
    {
        Assert.Throws<ArgumentNullException>(() => Markup.Attr(null!, "id"));
        Assert.Throws<ArgumentNullException>(() => Markup.Attr("<div>", null!));
        Assert.Throws<ArgumentNullException>(() => Markup.Attrs(null!, "id"));
        Assert.Throws<ArgumentNullException>(() => Markup.Attrs("<div>", null!));
    }
}
