using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.ScopedCss;
using Rask.Core.ScopedJs;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.ScopedJs;

[Collection("ScopedJs")]
public class ScopedJsRenderTests
{
    public ScopedJsRenderTests()
    {
        ScopedJsRegistry.InvalidateAll();
        ScopedCssRegistry.InvalidateAll();
    }

    [Fact]
    public void Render_ComponentWithJs_StampsMountAttributeOnRootElement()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");

        var view = new JsWrapper(Div(Class: "tag")[Span()[Text("hi")]]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(JsWrapper));

        // The outermost element of the component's render gets data-rask-mount.
        Assert.Contains($"data-rask-mount=\"{scopeId}\"", html);
        // Only ONE element carries the mount stamp — the root. Descendants don't.
        var firstIdx = html.IndexOf("data-rask-mount=", StringComparison.Ordinal);
        var nextIdx = html.IndexOf("data-rask-mount=", firstIdx + 1, StringComparison.Ordinal);
        Assert.Equal(-1, nextIdx);
    }

    [Fact]
    public void Render_ComponentWithoutJs_DoesNotStampMount()
    {
        var view = new NoJsWrapper(Div(Class: "tag")[Text("x")]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-rask-mount", html);
    }

    [Fact]
    public void Render_ComponentWithBothJsAndCss_StampsBothAttributes()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        ScopedCssRegistry.RegisterType(typeof(JsWrapper), ".tag { color: red; }");

        var view = new JsWrapper(Div(Class: "tag")[Span()[Text("hi")]]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(JsWrapper));

        // CSS data-{scopeId} stamps every descendant; JS data-rask-mount only stamps the root.
        Assert.Contains($"data-{scopeId}", html);
        Assert.Contains($"data-rask-mount=\"{scopeId}\"", html);
        Assert.Contains($"<span data-{scopeId}>", html);
    }

    [Fact]
    public void Render_ShellTagsDoNotGetMountStamp()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        var view = new JsWrapper(Style()[Text("body{}")]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-rask-mount", html);
    }

    [Fact]
    public void RaskScopedScripts_BeforeAnyJsRegistered_RendersEmpty()
    {
        var view = new NoJsWrapper(RaskScopedScripts());
        var html = view.RenderAsLiveRoot();
        Assert.Equal("", html);
    }

    [Fact]
    public void RaskScopedScripts_AfterJsRegistered_NoProvider_RendersEmpty()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        Assert.NotNull(ScopedJsRegistry.CurrentHash);

        var sp = new ServiceCollection().BuildServiceProvider();
        var probe = new NoJsWrapper(RaskScopedScripts());
        var html = probe.RenderAsLiveRoot(sp);
        Assert.Equal("", html);
    }

    [Fact]
    public void RaskScopedScripts_AfterJsRegistered_WithProvider_DelegatesToProvider()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        var hash = ScopedJsRegistry.CurrentHash;
        Assert.NotNull(hash);

        var services = new ServiceCollection();
        services.AddSingleton<IRaskScopedScripts>(
            new ScriptProvider(h => Script(Src: $"/_rask/scoped.js?v={h}", Defer: true)));
        var sp = services.BuildServiceProvider();

        var probe = new NoJsWrapper(RaskScopedScripts());
        var html = probe.RenderAsLiveRoot(sp);
        Assert.Contains($"src=\"/_rask/scoped.js?v={hash}\"", html);
        Assert.Contains(" defer", html);
    }

    private sealed class ScriptProvider : IRaskScopedScripts
    {
        private readonly Func<string, Component> _factory;
        public ScriptProvider(Func<string, Component> factory) => _factory = factory;
        public Component Render(string hash) => _factory(hash);
    }

    private sealed class NoJsWrapper : Component
    {
        private readonly Component _body;
        public NoJsWrapper(Component body) => _body = body;
        protected override Component Render() => _body;
    }

    private sealed class JsWrapper : Component
    {
        private readonly Component _body;
        public JsWrapper(Component body) => _body = body;
        protected override Component Render() => _body;
    }
}
