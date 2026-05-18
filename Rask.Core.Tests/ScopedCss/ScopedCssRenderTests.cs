using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssRenderTests
{
    public ScopedCssRenderTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void Render_ComponentWithCss_StampsScopeAttributeOnDescendants()
    {
        var view = new CssWrapper(Div(Class: "tag")[Span()[Text("hi")]]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        Assert.Contains($"<div class=\"tag\" data-{scopeId}>", html);
        Assert.Contains($"<span data-{scopeId}>hi</span>", html);
    }

    [Fact]
    public void Render_ComponentWithoutCss_DoesNotStamp()
    {
        var view = new NoCssWrapper(Div(Class: "tag")[Text("x")]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-r-", html);
    }

    [Fact]
    public void Render_ShellTags_NeverGetScopeAttribute()
    {
        var view = new CssWrapper(Style()[Text("body{}")]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(CssWrapper));
        Assert.DoesNotContain($"data-{scopeId}", html);
    }

    [Fact]
    public void Render_NestedScopedComponents_InnerSubtreeUsesInnerScope()
    {
        var inner = new OtherCssWrapper(Span(Class: "inner")[Text("i")]);
        var outer = new CssWrapper(Div(Class: "tag")[(Child)inner]);
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
        var view = new NoCssWrapper(RaskScopedStyles());
        var html = view.RenderAsLiveRoot();
        Assert.Equal("", html);
    }

    [Fact]
    public void RaskScopedStyles_AfterCssRegistered_NoProvider_RendersEmpty()
    {
        var view = new CssWrapper(Div(Class: "tag"));
        view.RenderAsLiveRoot();
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        var sp = new ServiceCollection().BuildServiceProvider();
        var probe = new NoCssWrapper(RaskScopedStyles());
        var html = probe.RenderAsLiveRoot(sp);
        Assert.Equal("", html);
    }

    [Fact]
    public void RaskScopedStyles_AfterCssRegistered_WithProvider_DelegatesToProvider()
    {
        var view = new CssWrapper(Div(Class: "tag"));
        view.RenderAsLiveRoot();
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);

        var services = new ServiceCollection();
        services.AddSingleton<IRaskScopedStyles>(
            new LinkProvider(h => Link(Rel: "stylesheet", Href: $"/_rask/scoped.css?v={h}")));
        var sp = services.BuildServiceProvider();

        var probe = new NoCssWrapper(RaskScopedStyles());
        var html = probe.RenderAsLiveRoot(sp);
        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
    }

    private sealed class LinkProvider : IRaskScopedStyles
    {
        private readonly Func<string, Component> _factory;
        public LinkProvider(Func<string, Component> factory) => _factory = factory;
        public Component Render(string hash) => _factory(hash);
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
