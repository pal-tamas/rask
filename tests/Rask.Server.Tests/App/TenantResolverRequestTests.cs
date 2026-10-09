using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>
///     An app's tenant resolver (<c>AddRaskTenant</c>) under the host: asked as an HTTP request starts and as a
///     live session opens, with that scope's services, and not again.
/// </summary>
[Collection(RaskAppCollection.Name)]
public sealed class TenantResolverRequestTests
{
    private const string TenantHeader = "X-Tenant";

    [Fact]
    public async Task An_endpoint_reads_the_tenant_the_resolver_named_for_its_request()
    {
        var asked = new Counter();
        var app = await StartAsync(asked);

        try
        {
            Assert.Equal("00000000-0000-0000-0000-00000000002a", await GetAsync(app, "/tenant", tenant: "42"));
            Assert.Equal("00000000-0000-0000-0000-000000000007", await GetAsync(app, "/tenant", tenant: "7"));
            Assert.Equal("none", await GetAsync(app, "/tenant", tenant: null));
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task The_resolver_is_asked_once_as_the_request_starts_whether_or_not_the_tenant_is_read()
    {
        var asked = new Counter();
        var app = await StartAsync(asked);

        try
        {
            await GetAsync(app, "/ping", tenant: "42");
            var afterAnEndpointThatReadsNothing = asked.Value;
            await GetAsync(app, "/tenant-twice", tenant: "42");

            Assert.Equal(1, afterAnEndpointThatReadsNothing);
            Assert.Equal(2, asked.Value);
        }
        finally
        {
            await app.StopAsync(TestContext.Current.CancellationToken);
        }
    }

    [Fact]
    public async Task A_live_session_is_asked_as_it_opens_and_keeps_that_answer()
    {
        var asked = new Counter();
        await using var services = new ServiceCollection()
            .AddRaskTenant(_ => asked.Next())
            .BuildServiceProvider(validateScopes: true);
        await using var session = services.CreateAsyncScope();
        var scope = new SessionDataScope();

        Guid? opened, later;
        using (scope.Enter(session.ServiceProvider))
        {
            opened = Current.Tenant;
        }

        using (scope.Enter(session.ServiceProvider))
        {
            later = Current.Tenant;
        }

        Assert.Equal(new Guid("00000000-0000-0000-0000-000000000001"), opened);
        Assert.Equal(opened, later);
        Assert.Equal(1, asked.Value);
    }

    private static async Task<WebApplication> StartAsync(Counter asked)
    {
        var app = RaskApp.Create(
            ["--applicationName", typeof(TenantResolverRequestTests).Assembly.GetName().Name!],
            builder =>
            {
                builder.WebHost.UseSetting("urls", "http://127.0.0.1:0");
                builder.Services.AddHttpContextAccessor();

                // The shape an app writes: the tenant read off the request, as the number its rows hold.
                builder.Services.AddRaskTenant(sp =>
                {
                    asked.Next();
                    var header = sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers[TenantHeader];
                    return int.TryParse(header, out var tenant) ? tenant : (int?)null;
                });
            });

        app.MapEndpoints(endpoints =>
        {
            endpoints.MapGet("/ping", () => "pong");
            endpoints.MapGet("/tenant", () => Current.Tenant?.ToString() ?? "none");
            endpoints.MapGet("/tenant-twice", () => $"{Current.Tenant}|{Current.Tenant}");
        });

        var built = app.Build<MinimalApp>();
        await built.StartAsync();
        return built;
    }

    private static async Task<string> GetAsync(WebApplication app, string path, string? tenant)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        if (tenant is not null)
        {
            request.Headers.Add(TenantHeader, tenant);
        }

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);
        return await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
    }

    private sealed class Counter
    {
        private int _value;

        public int Value => Volatile.Read(ref _value);

        public int Next() => Interlocked.Increment(ref _value);
    }
}
