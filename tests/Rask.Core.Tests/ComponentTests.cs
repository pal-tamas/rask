#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests;

public class ComponentTests
{
    [Fact]
    public void Render_NullProps_NoChildren_ReturnsOpenAndCloseTags() =>
        Assert.Equal("<block></block>", new Block().ToHtml());

    [Fact]
    public void Render_NullProps_WithStringChild_EncodesChildText() =>
        Assert.Equal("<block>&lt;x&gt;</block>", new Block()["<x>"].ToHtml());

    [Fact]
    public void Render_PropsWithStringValue_EmitsQuotedAttribute() =>
        Assert.Equal(
            "<block class=\"x\"></block>",
            new Block { Class = "x" }.ToHtml());

    [Fact]
    public void Render_AttributeValueWithSpecials_EncodesValue() =>
        Assert.Equal(
            "<block class=\"a&quot;b&lt;c\"></block>",
            new Block { Class = "a\"b<c" }.ToHtml());

    [Fact]
    public void Render_BooleanLikeAttribute_NullValue_EmitsBareName()
    {
        var data = new Dictionary<string, string?> { ["flag"] = null };
        var html = new Block { Data = data }.ToHtml();

        Assert.Equal("<block data-flag></block>", html);
    }

    [Fact]
    public void Render_MultipleChildren_RendersInDeclarationOrder()
    {
        var html = new Block()[Text("a"), Raw("<i>"), Text("b")].ToHtml();
        Assert.Equal("<block>a<i>b</block>", html);
    }

    [Fact]
    public void Render_NullChildrenArgument_TreatedAsEmpty()
    {
        var html = new Block().ToHtml();
        Assert.Equal("<block></block>", html);
    }

    [Fact]
    public void Render_SelfClosing_NoChildren_ReturnsSelfClosingTag() =>
        Assert.Equal("<void />", new VoidEl().ToHtml());

    [Fact]
    public void Render_SelfClosing_WithChildrenSupplied_StillReturnsSelfClosingAndIgnoresChildren()
    {
        var html = new VoidEl()[Text("ignored")].ToHtml();
        Assert.Equal("<void />", html);
    }

    [Fact]
    public void Render_SelfClosing_WithAttributes_PlacesAttributesBeforeSelfCloser() =>
        Assert.Equal(
            "<void class=\"a\" />",
            new VoidEl { Class = "a" }.ToHtml());

    [Fact]
    public void Indexer_AssignsChildren_AndReturnsThis()
    {
        var div = Div();
        var returned = div[Text("a")];

        Assert.Same(div, returned);
        Assert.NotNull(div.Children);
        Assert.Single(div.Children!);
    }

    [Fact]
    public void Indexer_RenderedHtml_ContainsChildren()
    {
        var html = Div()[Span(), "hi"].ToHtml();
        Assert.Equal("<div><span></span>hi</div>", html);
    }

    [Fact]
    public void Indexer_AcceptsStringAndComponent_ViaImplicitChild()
    {
        // Both `"text"` and `Component` instances flow through the indexer thanks to the
        // implicit Component conversions; the indexer signature is `params IEnumerable<Component>`.
        var html = Div()["before", Strong()["bold"], "after"].ToHtml();
        Assert.Equal("<div>before<strong>bold</strong>after</div>", html);
    }

    private sealed class Block : Element
    {
        protected override string TagName => "block";
    }

    private sealed class VoidEl : Element
    {
        protected override string TagName => "void";
        protected override bool SelfClosing => true;
    }
}
