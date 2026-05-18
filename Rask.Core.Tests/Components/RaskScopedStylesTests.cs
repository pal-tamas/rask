using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Components;
using Rask.Core.ScopedCss;
using Rask.Core.Tests.Live;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

[Collection("ScopedCss")]
public class RaskScopedStylesTests
{
    public RaskScopedStylesTests()
    {
        ScopedCssRegistry.InvalidateAll();
        ScopedCssRegistry.RegisterType(typeof(RedWrapper), ".red { color: red; }");
    }

    [Fact]
    public void Render_NoHashRegistered_EmitsEmptyRaw()
    {
        var html = RaskScopedStyles().ToHtml();
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_HashRegistered_NoProvider_EmitsEmptyRaw()
    {
        new RedWrapper(Div()).RenderAsLiveRoot();
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        var sp = new ServiceCollection().BuildServiceProvider();
        var view = new StubComponent(() => RaskScopedStyles());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_ProviderRegistered_DelegatesToProviderRenderWithCurrentHash()
    {
        new RedWrapper(Div()).RenderAsLiveRoot();
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);

        var captured = (string?)null;
        var services = new ServiceCollection();
        services.AddSingleton<IRaskScopedStyles>(
            new MockScopedStylesProvider(h =>
            {
                captured = h;
                return Raw($"<link data-h=\"{h}\"/>");
            }));
        var sp = services.BuildServiceProvider();
        var view = new StubComponent(() => RaskScopedStyles());

        var html = view.RenderAsLiveRoot(sp);

        Assert.Equal(hash, captured);
        Assert.Equal($"<link data-h=\"{hash}\"/>", html);
    }

    [Fact]
    public void Render_NoLiveContext_EmitsEmptyRaw()
    {
        // Direct ToHtml() call without a LiveRenderContext exercises the
        // `LiveRenderContext.Current?.Services` null-conditional branch.
        new RedWrapper(Div()).RenderAsLiveRoot();
        Assert.NotNull(ScopedCssRegistry.CurrentHash);

        var html = RaskScopedStyles().ToHtml();

        Assert.Equal(string.Empty, html);
    }

    private sealed class MockScopedStylesProvider : IRaskScopedStyles
    {
        private readonly Func<string, Component> _factory;
        public MockScopedStylesProvider(Func<string, Component> factory) => _factory = factory;
        public Component Render(string hash) => _factory(hash);
    }

    private sealed class RedWrapper : Component
    {
        private readonly Component _body;
        public RedWrapper(Component body) => _body = body;
        protected override Component Render() => _body;
    }
}
