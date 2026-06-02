using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class RaskRuntimeScriptTests
{
    // RaskRuntimeScript is now a deprecated no-op: the runtime <script> is injected
    // automatically at the end of <body> by HtmlSerializer (see RuntimeScriptInjectionTests).
    // The component renders nothing regardless of whether a provider is registered, so a
    // legacy tree that still contains it does not double-emit the script.

    [Fact]
    public void Render_NoProviderRegistered_EmitsEmpty()
    {
        var sp = RenderHarness.EmptyServices();
        var view = new StubComponent(() => RaskRuntimeScript());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_EvenWithProviderRegistered_EmitsEmpty()
    {
        // Proves the component no longer delegates to IRaskRuntimeScript — emission moved
        // to the body-close hook in HtmlSerializer.
        var services = new ServiceCollection();
        services.AddSingleton<IRaskRuntimeScript>(
            new StubRuntimeScriptProvider(Raw("<script src=\"/x.js\"></script>")));
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => RaskRuntimeScript());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_NoLiveContext_EmitsEmpty()
    {
        var html = RaskRuntimeScript().ToHtml();

        Assert.Equal(string.Empty, html);
    }

    private sealed class StubRuntimeScriptProvider : IRaskRuntimeScript
    {
        private readonly Component _component;
        public StubRuntimeScriptProvider(Component component) => _component = component;
        public Component Render() => _component;
    }
}
