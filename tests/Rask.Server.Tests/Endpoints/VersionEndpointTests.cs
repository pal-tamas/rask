using System.Net;
using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
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

    [Fact]
    public async Task The_configured_release_answers_before_the_assembly_version()
    {
        await using var app = await Host(
            e => e.MapRaskVersion("/version", typeof(VersionEndpointTests).Assembly),
            settings: new Dictionary<string, string?>(StringComparer.Ordinal) { ["Rask:Version"] = "v26.11.11.13.12.02" });

        using var response = await app.GetTestClient().GetAsync("/version", TestContext.Current.CancellationToken);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);

        Assert.Equal("""{"version":"v26.11.11.13.12.02"}""", body);
    }

    [Fact]
    public void A_configured_release_wins_over_version_json_and_the_assembly()
    {
        using var folder = new VersionFolder("""{"version":"from-file"}""");

        var version = RaskVersionEndpointExtensions.VersionOf("v26.11.11.13.12.02", folder.Path, typeof(VersionEndpointTests).Assembly);

        Assert.Equal("v26.11.11.13.12.02", version);
    }

    [Fact]
    public void Version_json_beside_the_app_answers_when_nothing_is_configured()
    {
        using var folder = new VersionFolder("""{ "version": "2026.11.11-build.42" }""");

        var version = RaskVersionEndpointExtensions.VersionOf(" ", folder.Path, typeof(VersionEndpointTests).Assembly);

        Assert.Equal("2026.11.11-build.42", version);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":42}""")]
    [InlineData("""["v1"]""")]
    [InlineData("""{"version":""}""")]
    public void A_version_json_of_another_shape_falls_back_to_the_assembly_version(string content)
    {
        using var folder = new VersionFolder(content);

        var version = RaskVersionEndpointExtensions.VersionOf(null, folder.Path, typeof(VersionEndpointTests).Assembly);

        Assert.Equal(RaskVersionEndpointExtensions.VersionOf(typeof(VersionEndpointTests).Assembly), version);
    }

    [Fact]
    public void No_version_json_falls_back_to_the_assembly_version()
    {
        using var folder = new VersionFolder(content: null);

        var version = RaskVersionEndpointExtensions.VersionOf(null, folder.Path, typeof(VersionEndpointTests).Assembly);

        Assert.Equal(RaskVersionEndpointExtensions.VersionOf(typeof(VersionEndpointTests).Assembly), version);
    }

    // A throwaway folder standing in for the app's own, holding version.json when there is content for it.
    private sealed class VersionFolder : IDisposable
    {
        public VersionFolder(string? content)
        {
            Path = Directory.CreateTempSubdirectory("rask-version-").FullName;
            if (content is not null)
            {
                File.WriteAllText(System.IO.Path.Combine(Path, RaskVersionEndpointExtensions.FileName), content);
            }
        }

        public string Path { get; }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // A host wired by hand: AddRask, the endpoint, then MapRask's catch-all, which must leave the path alone.
    private static async Task<WebApplication> Host(
        Action<WebApplication> map, Action<IServiceCollection>? services = null, Dictionary<string, string?>? settings = null)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        if (settings is not null)
        {
            builder.Configuration.AddInMemoryCollection(settings);
        }

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
