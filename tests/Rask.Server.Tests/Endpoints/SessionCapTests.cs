using System.Net;
using Rask.Server.Tests.Infrastructure;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     RaskLiveOptions.MaxSessions backstop on the catch-all GET endpoint. Unbounded session
///     creation is a memory-exhaustion surface; once the cap is reached a new GET is refused
///     with 503 + Retry-After instead of minting another session. The cap lives on the store
///     instance (not a static), so setting it here can't bleed into other parallel tests.
/// </summary>
public class SessionCapTests
{
    [Fact]
    public async Task Default_Unlimited_AdmitsManySessions()
    {
        using var host = RaskTestHost.Create<TestApp>();
        Assert.Equal(0, host.Store.MaxSessions);

        for (var i = 0; i < 5; i++)
        {
            (await host.Http.GetAsync($"/p{i}")).EnsureSuccessStatusCode();
        }

        Assert.Equal(5, host.Store.Count);
    }

    [Fact]
    public async Task OverCap_NewSession_Returns503WithRetryAfter()
    {
        using var host = RaskTestHost.Create<TestApp>();
        host.Store.MaxSessions = 1;

        var first = await host.Http.GetAsync("/first");
        first.EnsureSuccessStatusCode();
        Assert.Equal(1, host.Store.Count);

        var second = await host.Http.GetAsync("/second");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, second.StatusCode);
        Assert.True(second.Headers.TryGetValues("Retry-After", out var retry));
        Assert.Equal("5", Assert.Single(retry));
        Assert.Equal(1, host.Store.Count); // no extra session minted
    }
}
