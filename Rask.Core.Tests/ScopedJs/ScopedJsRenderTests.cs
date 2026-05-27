using Microsoft.Extensions.DependencyInjection;
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
    public void Render_ComponentWithJs_DoesNotStampMountAttribute()
    {
        // Under the post-`Component.InvokeJs` model, scoped-JS modules register on
        // `window.Rask.{TypeName}` and user JS queries the DOM by user-controlled
        // classes. Rask no longer stamps data-rask-mount — there's nothing on the
        // client looking for it.
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");

        var view = new JsWrapper(Div(Class: "tag")[Span()[Text("hi")]]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-rask-mount", html);
    }

    [Fact]
    public void Render_ComponentWithoutJs_DoesNotStampMount()
    {
        var view = new NoJsWrapper(Div(Class: "tag")[Text("x")]);
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("data-rask-mount", html);
    }

    [Fact]
    public void Render_ComponentWithBothJsAndCss_StampsCssAttributeOnly()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        ScopedCssRegistry.RegisterType(typeof(JsWrapper), ".tag { color: red; }");

        var view = new JsWrapper(Div(Class: "tag")[Span()[Text("hi")]]);
        var html = view.RenderAsLiveRoot();
        var scopeId = CssScoper.ScopeIdFor(typeof(JsWrapper));

        // CSS data-{scopeId} stamps every descendant; data-rask-mount is gone.
        Assert.Contains($"data-{scopeId}", html);
        Assert.DoesNotContain("data-rask-mount", html);
        Assert.Contains($"<span data-{scopeId}>", html);
    }

    [Fact]
    public void ScopedJs_BeforeAnyJsRegistered_EmitsNothingInHead()
    {
        var view = new PageRoot(new NoJsWrapper(Div(Class: "x")));
        var html = view.RenderAsLiveRoot();
        Assert.DoesNotContain("scoped.js", html);
        Assert.DoesNotContain("__rask_head_assets__", html);
    }

    [Fact]
    public void ScopedJs_AfterJsRegistered_NoProvider_EmitsNothingInHead()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        var view = new PageRoot(new JsWrapper(Div(Class: "x")));
        var sp = new ServiceCollection().BuildServiceProvider();
        var html = view.RenderAsLiveRoot(sp);
        Assert.NotNull(ScopedJsRegistry.CurrentHash);
        Assert.DoesNotContain("scoped.js", html);
    }

    [Fact]
    public void ScopedJs_AfterJsRegistered_WithProvider_ScriptAppearsInHead()
    {
        ScopedJsRegistry.RegisterType(typeof(JsWrapper), "export function rendered(el) {}");
        var services = new ServiceCollection();
        services.AddSingleton<IRaskScopedScripts>(
            new ScriptProvider(h => Script($"/_rask/scoped.js?v={h}", Defer: true)));
        var sp = services.BuildServiceProvider();

        var view = new PageRoot(new JsWrapper(Div(Class: "x")));
        var html = view.RenderAsLiveRoot(sp);
        var hash = ScopedJsRegistry.CurrentHash;
        Assert.NotNull(hash);
        Assert.Contains($"src=\"/_rask/scoped.js?v={hash}\"", html);
        Assert.Contains(" defer", html);
        var headOpen = html.IndexOf("<head>", StringComparison.Ordinal);
        var headClose = html.IndexOf("</head>", StringComparison.Ordinal);
        var scriptIdx = html.IndexOf("scoped.js", StringComparison.Ordinal);
        Assert.True(headOpen < scriptIdx && scriptIdx < headClose);
    }

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
        protected override RenderResult Render() => _body;
    }

    private sealed class JsWrapper : Component
    {
        private readonly Component _body;
        public JsWrapper(Component body) => _body = body;
        protected override RenderResult Render() => _body;
    }
}
