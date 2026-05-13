using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

public class RaskRuntimeScriptTests
{
    [Fact]
    public void Render_NoProviderRegistered_EmitsEmptyRaw()
    {
        var sp = new ServiceCollection().BuildServiceProvider();
        var view = new StubComponent(() => RaskRuntimeScript());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_ProviderRegistered_DelegatesToProviderRender()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRaskRuntimeScript>(
            new MockRuntimeScriptProvider(Raw("<script src=\"/x.js\"></script>")));
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => RaskRuntimeScript());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal("<script src=\"/x.js\"></script>", html);
    }

    [Fact]
    public void Render_NoLiveContext_EmitsEmptyRaw()
    {
        // Direct ToHtml() call without a LiveRenderContext exercises the
        // `LiveRenderContext.Current?.Services` null-conditional branch.
        var html = RaskRuntimeScript().ToHtml();

        Assert.Equal(string.Empty, html);
    }

    private sealed class MockRuntimeScriptProvider : IRaskRuntimeScript
    {
        private readonly Component _component;
        public MockRuntimeScriptProvider(Component component) => _component = component;
        public Component Render() => _component;
    }
}
