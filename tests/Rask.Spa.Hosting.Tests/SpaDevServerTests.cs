using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
///     A React, Vue or Angular app VS Code's F5 launched runs the bundler's own dev server itself — the one
///     <c>rask dev</c> would have started beside the host — because a dev-session build skipped the production
///     bundle and there would otherwise be nothing to serve.
/// </summary>
public sealed class SpaDevServerTests
{
    private static readonly System.Reflection.Assembly DevSessionBuild = typeof(SpaDevServerTests).Assembly;

    [Fact]
    public void An_editor_session_runs_the_script_the_client_names_where_the_build_found_it()
    {
        var client = Path.Combine("app", "client");

        var plan = SpaDevServer.PlanFor(
            editorSession: true,
            Metadata(client, "http://localhost:4200"),
            path => path == Path.Combine(client, "package.json") ? """{ "scripts": { "start": "ng serve" } }""" : null);

        Assert.NotNull(plan);
        Assert.Equal(client, plan!.ClientDirectory);
        Assert.Equal("start", plan.Script);                 // the Angular CLI's name for it
        Assert.Equal("http://localhost:4200", plan.Url);    // where the browser goes, and the port it waits on
    }

    [Fact]
    public void A_client_that_names_no_dev_server_gets_vites_default()
    {
        // The same assumption `rask dev` makes for a scaffold too old to have baked the URL into its csproj.
        var plan = SpaDevServer.PlanFor(editorSession: true, Metadata("client", url: null), _ => null);

        Assert.Equal(SpaDevServer.DefaultDevServerUrl, plan!.Url);
        Assert.Equal("dev", plan.Script);
    }

    [Fact]
    public void Anything_but_an_editor_session_runs_nothing()
    {
        // Under `rask dev` the CLI already runs this server; in production there is no dev server at all.
        Assert.Null(SpaDevServer.PlanFor(editorSession: false, Metadata("client", "http://localhost:5173"), _ => "{}"));
    }

    [Fact]
    public void A_build_that_resolved_no_client_runs_nothing()
    {
        Assert.Null(SpaDevServer.PlanFor(editorSession: true, Metadata(client: null, url: null), _ => "{}"));
    }

    [Fact]
    public void A_dev_session_build_registers_the_server_once()
    {
        var services = new ServiceCollection();

        SpaDevServer.Register(services, DevSessionBuild);
        SpaDevServer.Register(services, DevSessionBuild);

        Assert.Single(services, d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(SpaDevServer));
    }

    [Fact]
    public void An_ordinary_build_registers_nothing()
    {
        var services = new ServiceCollection();

        SpaDevServer.Register(services, typeof(object).Assembly);

        Assert.Empty(services);
    }

    private static Func<string, string?> Metadata(string? client, string? url) => key =>
        key == SpaAppBundle.ClientMetadataKey ? client
        : key == SpaAppBundle.DevServerMetadataKey ? url
        : null;
}
