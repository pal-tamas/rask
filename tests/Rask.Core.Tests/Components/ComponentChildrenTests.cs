#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

// Guards the child model after RenderResult/Child were removed and Component became both the child
// element type and a collection-expression target. The nesting test is load-bearing: it proves a
// bare component passed to the children indexer is treated as ONE child (via `this[params
// Component?[]]`) and NOT flattened into its own children — which is exactly what would happen if
// Component implemented IEnumerable<Component> instead of exposing only the pattern GetEnumerator.
public partial class ComponentChildrenTests : global::Rask.Core.RaskMarkup
{
    [Fact]
    public void NestedComponentChild_IsWrappedNotFlattened()
    {
        // If `this[IEnumerable<Component?>]` won the overload, this would collapse to "<div>hi</div>".
        Assert.Equal("<div><span>hi</span></div>", Div[Span["hi"]].ToHtml());
    }

    [Fact]
    public void HeterogeneousLiterals_ConvertToTextViaComponentOperators()
    {
        // string / int / bool literals flow in through the implicit converters now on Component.
        Assert.Equal("<div>a42True<span>x</span></div>", Div["a", 42, true, Span["x"]].ToHtml());
    }

    [Fact]
    public void NullChild_RendersNothing()
    {
        Assert.Equal("<div>ab</div>", Div["a", null, "b"].ToHtml());
    }

    [Fact]
    public void CollectionExpressionChild_GroupsWithoutWrappingTag()
    {
        // A nested `[...]` in the indexer builds a tagless container (Fragment) via Component.Create.
        Assert.Equal("<div><span>a</span><span>b</span></div>",
            Div[[Span["a"], Span["b"]]].ToHtml());
    }

    [Fact]
    public void RenderReturningCollectionExpression_EmitsAllRoots()
    {
        Assert.Equal("<!DOCTYPE html><html></html>", new MultiRoot().ToHtml());
    }

    [Fact]
    public void RenderReturningNull_EmitsNothing()
    {
        Assert.Equal("", new RendersNothing().ToHtml());
    }

    private sealed class MultiRoot : Component
    {
        protected override Component? Render() => [Doctype, Html];
    }

    private sealed class RendersNothing : Component
    {
        protected override Component? Render() => null;
    }
}
