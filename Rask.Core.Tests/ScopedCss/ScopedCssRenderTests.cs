using Microsoft.Extensions.DependencyInjection;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssRenderTests
{
    public ScopedCssRenderTests()
    {
        ScopedCssRegistry.InvalidateAll();
        ScopedCssRegistry.RegisterType(typeof(CssWrapper), ".tag { color: red; }");
        ScopedCssRegistry.RegisterType(typeof(OtherCssWrapper), ".inner { color: blue; }");
    }

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
    public void ScopedCss_BeforeAnyCssRegistered_EmitsNothingInHead()
    {
        ScopedCssRegistry.InvalidateAll();
        var view = new PageRoot(new NoCssWrapper(Div(Class: "tag")));
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("scoped.css", html);
        Assert.DoesNotContain("__rask_head_assets__", html);
    }

    [Fact]
    public void ScopedCss_AfterCssRegistered_NoProvider_EmitsNothingInHead()
    {
        // The framework only emits the scoped-css <link> when the host has
        // registered an IRaskScopedStyles strategy. Without one, the bundle exists
        // (CurrentHash non-null) but no link tag lands in <head>.
        var view = new PageRoot(new CssWrapper(Div(Class: "tag")));
        var sp = new ServiceCollection().BuildServiceProvider();
        var html = view.RenderAsLiveRoot(sp);
        Assert.NotNull(ScopedCssRegistry.CurrentHash);
        Assert.DoesNotContain("scoped.css", html);
    }

    [Fact]
    public void ScopedCss_AfterCssRegistered_WithProvider_LinkAppearsInHead()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRaskScopedStyles>(
            new LinkProvider(h => Link(Rel: "stylesheet", Href: $"/_rask/scoped.css?v={h}")));
        var sp = services.BuildServiceProvider();

        var view = new PageRoot(new CssWrapper(Div(Class: "tag")));
        var html = view.RenderAsLiveRoot(sp);
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);
        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
        // Link sits inside <head>.
        var headOpen = html.IndexOf("<head>", StringComparison.Ordinal);
        var headClose = html.IndexOf("</head>", StringComparison.Ordinal);
        var linkIdx = html.IndexOf("scoped.css", StringComparison.Ordinal);
        Assert.True(headOpen < linkIdx && linkIdx < headClose);
    }

    // Minimal page root with a framework-managed <head>. Used to exercise the
    // auto-emission path now that the RaskScopedStyles marker is gone.
    private sealed class PageRoot : Component
    {
        private readonly Component _body;
        public PageRoot(Component body) => _body = body;

        protected override RenderResult Render() =>
            Fragment()[
                Doctype(),
                Html("en")[
                    Head(),
                    Body()[_body]
                ]
            ];
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
        protected override RenderResult Render() => _body;
    }

    private sealed class CssWrapper : Component
    {
        private readonly Component _body;
        public CssWrapper(Component body) => _body = body;
        protected override RenderResult Render() => _body;
    }

    private sealed class OtherCssWrapper : Component
    {
        private readonly Component _body;
        public OtherCssWrapper(Component body) => _body = body;
        protected override RenderResult Render() => _body;
    }
}
