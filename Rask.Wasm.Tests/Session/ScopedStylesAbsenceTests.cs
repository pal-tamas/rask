using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Core.ScopedCss;
using static Rask.Core.Components.Components;

#pragma warning disable RASK019 // test-infra apps predate framework-managed <head>

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Wasm.Tests.Session;

/// <summary>
///     Regression: under sub-path hosting (e.g. GitHub Pages at /rask/), the absolute
///     <c>/_rask/scoped.css?v=…</c> link bypasses <c>&lt;base href&gt;</c> and 404s — and
///     it's redundant anyway because WASM applies the bundle inline via the
///     <c>&lt;style id="rask-scoped" data-rask-managed&gt;</c> mechanism. WASM hosts
///     deliberately don't register <see cref="IRaskScopedStyles" />, so
///     <see cref="RaskScopedStyles" /> must render nothing.
/// </summary>
[Collection("WasmSession")]
public class ScopedStylesAbsenceTests
{
    public ScopedStylesAbsenceTests()
    {
        ScopedCssRegistry.InvalidateAll();
        ScopedCssRegistry.RegisterType(typeof(ScopedCssStubApp), ".tag { color: red; }");
    }

    [Fact]
    public async Task InitialRender_AppWithScopedCss_DoesNotIncludeScopedCssLink()
    {
        var (session, _) = NewSession();

        var payload = await session.InitialRenderAsync();

        using var doc = JsonDocument.Parse(payload.AsMemory());
        var html = doc.RootElement.GetProperty("html").GetString()!;
        Assert.DoesNotContain("/_rask/scoped.css", html);
        Assert.DoesNotContain("data-rask-scoped", html);
        // Sanity: the CSS bundle still gets to the runtime — just inline, not as a link.
        var cssText = doc.RootElement.GetProperty("cssText").GetString();
        Assert.NotNull(cssText);
        Assert.Contains(".tag", cssText);
    }

    private static (WasmLiveSession session, IServiceProvider services) NewSession()
    {
        var services = new ServiceCollection();
        services.AddSingleton<RouteState>();
        services.AddSingleton<Navigator>();
        var provider = services.BuildServiceProvider();
        var app = ActivatorUtilities.CreateInstance<ScopedCssStubApp>(provider);
        var session = new WasmLiveSession(app, provider);
        JSInterop.Init(session);
        return (session, provider);
    }

    private sealed class ScopedCssStubApp : Component
    {
        protected override Component? Head => Title()["wasm-stub"];

        protected override Component Render() =>
            Fragment()[
                Doctype(),
                Html()[
                    Head(),
                    Body()[Div(Class: "tag")["hi"]]
                ]
            ];
    }
}
