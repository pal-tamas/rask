using Rask.Core.Components;
using Rask.Core.ScopedCss;

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssRenderTests
{
    public ScopedCssRenderTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void Render_ComponentWithCss_StampsScopeAttributeOnDescendants()
    {
        var view = new CssWrapper(new Div(new Div.Props(Class: "tag"), new Span(new Span.Props(), new Text("hi"))));
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        Assert.Contains($"<div class=\"tag\" data-{scopeId}>", html);
        Assert.Contains($"<span data-{scopeId}>hi</span>", html);
    }

    [Fact]
    public void Render_ComponentWithoutCss_DoesNotStamp()
    {
        var view = new NoCssWrapper(new Div(new Div.Props(Class: "tag"), new Text("x")));
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-r-", html);
    }

    [Fact]
    public void Render_ShellTags_NeverGetScopeAttribute()
    {
        var view = new CssWrapper(new Style(new Style.Props(), new Text("body{}")));
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        Assert.DoesNotContain($"data-{scopeId}", html);
    }

    [Fact]
    public void Render_NestedScopedComponents_InnerSubtreeUsesInnerScope()
    {
        var inner = new OtherCssWrapper(new Span(new Span.Props(Class: "inner"), new Text("i")));
        var outer = new CssWrapper(new Div(new Div.Props(Class: "tag"), (Child)inner));
        var html = outer.RenderAsLiveRoot();
        var outerId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        var innerId = CssScoper.ScopeIdFor(typeof(OtherCssWrapper));
        Assert.Contains($"<div class=\"tag\" data-{outerId}>", html);
        Assert.Contains($"<span class=\"inner\" data-{innerId}>i</span>", html);
        Assert.DoesNotContain($"data-{outerId}>i", html);
    }

    [Fact]
    public void RaskScopedStyles_BeforeAnyCssRegistered_RendersEmpty()
    {
        ScopedCssRegistry.InvalidateAll();
        var view = new NoCssWrapper(new RaskScopedStyles());
        var html = view.RenderAsLiveRoot();
        Assert.Equal("", html);
    }

    [Fact]
    public void RaskScopedStyles_AfterCssRegistered_RendersLink()
    {
        var view = new CssWrapper(new Div(new Div.Props(Class: "tag"), null));
        view.RenderAsLiveRoot();
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);

        var probe = new NoCssWrapper(new RaskScopedStyles());
        var html = probe.RenderAsLiveRoot();
        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
        Assert.Contains("data-rask-scoped", html);
    }

    private sealed class NoCssWrapper : Component
    {
        private readonly Component _body;
        public NoCssWrapper(Component body) => _body = body;
        protected override Component Render() => _body;
    }

    private sealed class CssWrapper : Component
    {
        private readonly Component _body;
        public CssWrapper(Component body) => _body = body;
        protected internal override string? Css => ".tag { color: red; }";
        protected override Component Render() => _body;
    }

    private sealed class OtherCssWrapper : Component
    {
        private readonly Component _body;
        public OtherCssWrapper(Component body) => _body = body;
        protected internal override string? Css => ".inner { color: blue; }";
        protected override Component Render() => _body;
    }
}
