using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Rask.Server.Tests.App;

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     <c>MapRaskVersion()</c> answers the running build's informational version as JSON, to anyone, never cached —
///     and beside <c>MapRask</c>'s catch-all, which must not swallow it.
/// </summary>
public class VersionEndpointTests
{
    [Fact]
    public async Task The_version_endpoint_answers_the_informational_version_as_json()
    {
        await using var app = await Host(e => e.MapRaskVersion("/version", typeof(VersionEndpointTests).Assembly));
        var expected = typeof(VersionEndpointTests).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;

        using var response = await app.GetTestClient().GetAsync("/version", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var json = JsonDocument.Parse(body);
        Assert.Equal(expected, json.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public async Task The_version_is_never_cached()
    {
        await using var app = await Host(e => e.MapRaskVersion());

        using var response = await app.GetTestClient().GetAsync("/version", TestContext.Current.CancellationToken);

        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task The_version_answers_without_signing_in_behind_a_fallback_policy()
    {
        await using var app = await Host(
            e => e.MapRaskVersion("/api/version"),
            services => services.AddAuthorizationBuilder()
                .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()));

        using var response = await app.GetTestClient().GetAsync("/api/version", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public void An_assembly_without_an_informational_version_falls_back_to_its_assembly_version()
    {
        var bare = new AssemblyName("Bare") { Version = new Version(2, 3, 4, 0) };
        var assembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(
            bare, System.Reflection.Emit.AssemblyBuilderAccess.Run);

        var version = RaskVersionEndpointExtensions.VersionOf(assembly);

        Assert.Equal("2.3.4.0", version);
    }

    [Fact]
    public void No_entry_assembly_answers_a_zero_version()
    {
        var version = RaskVersionEndpointExtensions.VersionOf(null);

        Assert.Equal("0.0.0", version);
    }

    // A host wired by hand: AddRask, the endpoint, then MapRask's catch-all, which must leave the path alone.
    private static async Task<WebApplication> Host(
        Action<WebApplication> map, Action<IServiceCollection>? services = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRask();
        services?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseAuthorization();
        map(app);
        app.MapRask<MinimalApp>();
        await app.StartAsync(TestContext.Current.CancellationToken);
        return app;
    }
}
