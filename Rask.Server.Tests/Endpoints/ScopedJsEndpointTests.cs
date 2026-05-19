using System.Net;
using Rask.Core.ScopedJs;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

[Collection("ScopedJs")]
public class ScopedJsEndpointTests
{
    public ScopedJsEndpointTests() => ScopedJsRegistry.InvalidateAll();

    [Fact]
    public async Task Get_NoIfNoneMatch_Returns200WithEtagAndCacheControl()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var response = await host.Http.GetAsync("/_rask/scoped.js");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-cache", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    [Fact]
    public async Task Get_MatchingIfNoneMatch_Returns304()
    {
        using var host = RaskTestHost.Create<TestApp>();

        var first = await host.Http.GetAsync("/_rask/scoped.js");
        var etag = first.Headers.ETag!.ToString();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/_rask/scoped.js");
        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        var second = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    [Fact]
    public async Task Get_NonMatchingIfNoneMatch_Returns200()
    {
        using var host = RaskTestHost.Create<TestApp>();

        using var req = new HttpRequestMessage(HttpMethod.Get, "/_rask/scoped.js");
        req.Headers.TryAddWithoutValidation("If-None-Match", "\"stale-hash\"");
        var response = await host.Http.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}

[CollectionDefinition("ScopedJs", DisableParallelization = true)]
public class ScopedJsCollectionDef
{
}
