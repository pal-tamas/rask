using System.Net;
using Rask.Core.ScopedCss;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

[Collection("ScopedCss")]
public class ScopedCssEndpointTests
{
    public ScopedCssEndpointTests() => ScopedCssRegistry.InvalidateAll();

    [Fact]
    public async Task Get_NoIfNoneMatch_Returns200WithEtagAndCacheControl()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/scoped.css");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("text/css", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Get_MatchingIfNoneMatch_Returns304()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var first = await host.Http.GetAsync("/_rask/scoped.css");
        var etag = first.Headers.ETag!.ToString();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/_rask/scoped.css");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Get_NonMatchingIfNoneMatch_Returns200()
    {
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/_rask/scoped.css");
        req.Headers.TryAddWithoutValidation("If-None-Match", "\"stale-hash\"");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

[CollectionDefinition("ScopedCss", DisableParallelization = true)]
public class ScopedCssCollectionDef
{
}
