using Rask.Core.Components;
using Rask.Core.ScopedCss;

namespace Rask.Core.Tests.Components;

[Collection("ScopedCss")]
public class RaskScopedStylesTests
{
    public RaskScopedStylesTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public void Render_NoHashRegistered_EmitsEmptyRaw()
    {
        var html = new RaskScopedStyles().ToHtml();
        Assert.Equal(string.Empty, html);
    }

    [Fact]
    public void Render_HashRegistered_EmitsLinkToScopedCssWithVersion()
    {
        new RedWrapper(new Div(new Div.Props())).RenderAsLiveRoot();
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);

        var html = new RaskScopedStyles().ToHtml();

        Assert.Contains($"href=\"/_rask/scoped.css?v={hash}\"", html);
        Assert.Contains("rel=\"stylesheet\"", html);
        Assert.Contains("data-rask-scoped", html);
    }

    [Fact]
    public void Render_HashChanges_HrefReflectsLatestHash()
    {
        new RedWrapper(new Div(new Div.Props())).RenderAsLiveRoot();
        var firstHash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(firstHash);

        ScopedCssRegistry.InvalidateAll();
        new BlueWrapper(new Div(new Div.Props())).RenderAsLiveRoot();
        var secondHash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(secondHash);
        Assert.NotEqual(firstHash, secondHash);

        var html = new RaskScopedStyles().ToHtml();

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
