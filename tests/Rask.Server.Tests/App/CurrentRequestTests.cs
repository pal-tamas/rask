using System.Security.Claims;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Server.Tests.App;

/// <summary>
///     <see cref="Current" /> inside a plain HTTP request — a minimal API, a controller, a CQRS endpoint — where
///     there is no live session and the principal is <c>HttpContext.User</c>.
/// </summary>
/// <remarks>
///     The request's scoped <c>IUserProvider</c> is an empty session provider nothing ever signs in, so a source
///     that read only that would answer "anonymous" for every API call from a signed-in user. These tests pin
///     that the request's own principal is the one read, and that it is read LATE: whatever the pipeline put on
///     <c>HttpContext.User</c> by the time the code asks.
/// </remarks>
[Collection(RaskAppCollection.Name)]
public sealed class CurrentRequestTests
{
    private static readonly Guid Alice = Guid.NewGuid();
    private static readonly Guid Acme = Guid.NewGuid();

    [Fact]
    public async Task An_endpoint_reads_the_signed_in_user_and_tenant_with_nothing_injected()
    {
        var app = await StartAsync();

        try
        {
            Assert.Equal($"{Alice}|{Acme}", await GetAsync(app, "/who?signed-in=1"));
        }
        finally
        {
            await app.StopAsync();
        }
    }

    [Fact]
    public async Task An_anonymous_request_has_no_user_and_the_ambient_ends_with_the_request()
    {
        var app = await StartAsync();

        try
        {
            Assert.Equal("nobody|none", await GetAsync(app, "/who"));

            // Nothing leaks out of the request into the flow that made it.
            Assert.Null(Current.Principal);
        }
        finally
        {
            await app.StopAsync();
        }
    }

    private static async Task<WebApplication> StartAsync()
    {
        var app = RaskApp.Create(
            ["--applicationName", typeof(CurrentRequestTests).Assembly.GetName().Name!],
            builder => builder.WebHost.UseSetting("urls", "http://127.0.0.1:0"));

        app.MapEndpoints(endpoints => endpoints.MapGet("/who", (HttpContext context) =>
        {
            // Stands in for an authentication handler: the principal arrives on HttpContext.User.
            if (context.Request.Query.ContainsKey("signed-in"))
            {
                context.User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Alice.ToString()),
                    new Claim(Tenant.ClaimType, Acme.ToString()),
                ], "Test"));
            }

            return $"{Current.UserId?.ToString() ?? "nobody"}|{Current.Tenant?.ToString() ?? "none"}";
        }));

        var built = app.Build<MinimalApp>();
        await built.StartAsync();
        return built;
    }

    private static async Task<string> GetAsync(WebApplication app, string path)
    {
        var address = app.Services.GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()!
            .Addresses.First();

        using var client = new HttpClient { BaseAddress = new Uri(address) };
        return await client.GetStringAsync(path);
    }
}
