using Rask.Core.Components;
using Rask.Core.ScopedCss;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories

namespace Rask.Core.Tests.Components;

[Collection("ScopedCss")]
public class RaskScopedStylesTests
{
    public RaskScopedStylesTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void Render_NoHashRegistered_EmitsEmptyRaw()
    {
        var html = RaskScopedStyles().ToHtml();
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_HashRegistered_EmitsLinkToScopedCssWithVersion()
    {
        new RedWrapper(Div()).RenderAsLiveRoot();
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);

        var html = RaskScopedStyles().ToHtml();

        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
        Assert.Contains("data-rask-scoped", html);
    }

    [Fact]
    public void Render_HashChanges_HrefReflectsLatestHash()
    {
        new RedWrapper(Div()).RenderAsLiveRoot();
        var firstHash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(firstHash);

        ScopedCssRegistry.InvalidateAll();
        new BlueWrapper(Div()).RenderAsLiveRoot();
        var secondHash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(secondHash);
        Assert.NotEqual(firstHash, secondHash);

        var html = RaskScopedStyles().ToHtml();

        Assert.Contains($"v={secondHash}", html);
        Assert.DoesNotContain($"v={firstHash}", html);
    }

    private sealed class RedWrapper : Component
    {
        private readonly Component _body;
        public RedWrapper(Component body) => _body = body;
        protected internal override string? Css => ".red { color: red; }";
        protected override Component Render() => _body;
    }

    private sealed class BlueWrapper : Component
    {
        private readonly Component _body;
        public BlueWrapper(Component body) => _body = body;
        protected internal override string? Css => ".blue { color: blue; }";
        protected override Component Render() => _body;
    }
}
