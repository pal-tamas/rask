using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Http;
using Rask.Core.Live;
using Rask.Core.ScopedCss;
using Rask.Server;

namespace Rask.Core.Tests.ScopedCss;

[Collection("ScopedCss")]
public class ScopedCssEndpointTests
{
    public ScopedCssEndpointTests() => ScopedCssRegistry.InvalidateAll();

    private static void Register(Component instance)
    {
        var m = typeof(ScopedCssRegistry).GetMethod(
            "TryRegister",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        m.Invoke(null, new object?[] { instance, null });
    }

    private static (HttpContext ctx, MemoryStream body) NewContext(string? ifNoneMatch = null)
    {
        var ctx = new DefaultHttpContext();
        var body = new MemoryStream();
        ctx.Response.Body = body;
        if (ifNoneMatch is not null)
        {
            ctx.Request.Headers.IfNoneMatch = ifNoneMatch;
        }

        return (ctx, body);
    }

    [Fact]
    public async Task ServeScopedCss_ReturnsBundleWithEtag()
    {
        Register(new WithCss());
        var (css, hash) = ScopedCssRegistry.GetBundle();
        var (ctx, body) = NewContext();

        await RaskEndpointExtensions.ServeScopedCssAsync(ctx);

        Assert.Equal(200, ctx.Response.StatusCode);
        Assert.Equal("text/css; charset=utf-8", ctx.Response.ContentType);
        Assert.Equal($"\"{hash}\"", ctx.Response.Headers.ETag.ToString());
        Assert.Equal("no-cache", ctx.Response.Headers.CacheControl.ToString());
        Assert.Equal(css, Encoding.UTF8.GetString(body.ToArray()));
    }

    [Fact]
    public async Task ServeScopedCss_IfNoneMatchHit_Returns304()
    {
        Register(new WithCss());
        var (_, hash) = ScopedCssRegistry.GetBundle();
        var (ctx, body) = NewContext($"\"{hash}\"");

        await RaskEndpointExtensions.ServeScopedCssAsync(ctx);

        Assert.Equal(304, ctx.Response.StatusCode);
        Assert.Equal(0, body.Length);
    }

    [Fact]
    public async Task ServeScopedCss_HashChanges_AfterNewRegistration()
    {
        Register(new WithCss());
        var hashA = ScopedCssRegistry.CurrentHash;

        Register(new EndpointSecond());
        var hashB = ScopedCssRegistry.CurrentHash;

        Assert.NotEqual(hashA, hashB);
        var (ctx, _) = NewContext();
        await RaskEndpointExtensions.ServeScopedCssAsync(ctx);
        Assert.Equal($"\"{hashB}\"", ctx.Response.Headers.ETag.ToString());
    }

    [Fact]
    public void BuildPayload_IncludesCssHash_SoLiveCanRefreshTheLink()
    {
        Register(new WithCss());
        var hash = ScopedCssRegistry.CurrentHash;
        Assert.NotNull(hash);
        var payload = LivePayload.BuildPayload("<body></body>", null, false);
        Assert.Contains($"\"cssHash\":\"{hash}\"", payload);
    }

    [Fact]
    public void BuildPayload_NoScopedCss_CssHashIsNull()
    {
        var payload = LivePayload.BuildPayload("<body></body>", null, false);
        Assert.Contains("\"cssHash\":null", payload);
    }

    private sealed class WithCss : Component
    {
        protected internal override string? Css => ".endpoint { color: red; }";
        public override Component Render() => this;
    }

    private sealed class EndpointSecond : Component
    {
        protected internal override string? Css => ".second { color: blue; }";
        public override Component Render() => this;
    }
}
