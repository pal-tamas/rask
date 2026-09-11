using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Endpoints;
using Rask.Server.DevTools;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests;

/// <summary>
///     The host script endpoint and the page stamp that names it, through the real <c>AddRask</c> /
///     <c>UseRask</c> wiring. The devtools are attached with the switch-independent
///     <see cref="RaskDevToolsLoader.Attach" />, because the unit gate builds Release, where the switch is off
///     and <c>AddRask</c>'s own attach does nothing.
/// </summary>
[Collection(DevToolsHookCollection.Name)]
public sealed class DevToolsServerEndpointTests
{
    [Fact]
    public async Task Development_serves_the_host_script()
    {
        using var host = Host("Development");

        var response = await host.Http.GetAsync(DevToolsServerEndpoints.HostScriptPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
        // A property name, so it survives Release minification.
        Assert.Contains("__raskDevtoolsHost", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public void Outside_development_nothing_is_mapped()
    {
        using var host = Host("Production");

        Assert.Null(HostScriptEndpoint(host, DevToolsServerEndpoints.HostScriptPath));
    }

    [Fact]
    public async Task The_host_script_is_served_under_the_path_base()
    {
        using var host = Host("Development", pathBase: "/sub");

        var response = await host.Http.GetAsync("/sub" + DevToolsServerEndpoints.HostScriptPath);

        response.EnsureSuccessStatusCode();
        Assert.Equal("text/javascript", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task A_fallback_authorization_policy_still_serves_the_host_script()
    {
        using var host = Host(
            "Development",
            services => services.AddAuthorization(o =>
                o.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build()),
            app => app.UseAuthorization());

        var endpoint = HostScriptEndpoint(host, DevToolsServerEndpoints.HostScriptPath);
        var response = await host.Http.GetAsync(DevToolsServerEndpoints.HostScriptPath);

        Assert.NotNull(endpoint?.Metadata.GetMetadata<IAllowAnonymous>());
        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public void The_embedded_host_script_is_the_bundle()
    {
        Assert.Contains("__raskDevtoolsHost", DevToolsScripts.LoadHost(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_development_page_loads_the_host_script_from_its_head()
    {
        using var host = Host("Development");

        var html = await host.Http.GetStringAsync("/");
        var tag = "<script src=\"" + DevToolsServerEndpoints.HostScriptPath + "\" data-panel=\"";
        var at = html.IndexOf(tag, StringComparison.Ordinal);

        Assert.True(at >= 0, "the page does not load the devtools host script:" + Environment.NewLine + html);
        Assert.True(at < html.IndexOf("</head>", StringComparison.Ordinal), "the host script tag is not in <head>");
        Assert.Equal(at, html.LastIndexOf(tag, StringComparison.Ordinal));
        Assert.Contains("data-rask-managed defer></script>", html[at..], StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_production_page_loads_no_host_script()
    {
        using var host = Host("Production");

        var html = await host.Http.GetStringAsync("/");

        // The page itself must still be live, or the absence below would prove nothing.
        Assert.Contains("data-rask-root=", html, StringComparison.Ordinal);
        Assert.DoesNotContain(DevToolsServerEndpoints.Prefix, html, StringComparison.Ordinal);
    }

    [Fact]
    public void The_stamp_follows_the_path_base_and_skips_the_devtools_own_pages()
    {
        using var host = Host("Development", pathBase: "/sub");
        var devTools = host.Services.GetRequiredService<IRaskServerDevTools>();

        var tag = devTools.PageTag(Request("/sub/"), "s1");
        Assert.NotNull(tag);
        Assert.Equal("/sub" + DevToolsServerEndpoints.HostScriptPath, tag.ScriptUrl);
        Assert.StartsWith("/sub" + DevToolsServerEndpoints.Prefix + "/?inspect=s1&t=", tag.PanelUrl, StringComparison.Ordinal);
        Assert.Null(devTools.PageTag(Request("/sub/_rask-devtools/"), "s1"));
        Assert.Null(devTools.PageTag(Request("/sub/_rask-devtools/panel"), "s1"));
        // A segment boundary, not a string prefix: an app page that merely starts with the same letters is inspected.
        Assert.NotNull(devTools.PageTag(Request("/sub/_rask-devtoolsx"), "s1"));
    }

    [Fact]
    public void Outside_development_no_page_names_the_host_script()
    {
        using var host = Host("Production");

        Assert.Null(host.Services.GetRequiredService<IRaskServerDevTools>().PageTag(Request("/"), "s1"));
    }

    private static RaskTestHost Host(
        string environment,
        Action<IServiceCollection>? services = null,
        Action<IApplicationBuilder>? middleware = null,
        string pathBase = "") =>
        RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s =>
            {
                RaskDevToolsLoader.Attach(s);
                services?.Invoke(s);
            },
            configureMiddleware: middleware,
            pathBase: pathBase,
            environment: environment);

    private static DefaultHttpContext Request(string path) => new() { Request = { Path = path } };

    private static RouteEndpoint? HostScriptEndpoint(RaskTestHost host, string path) =>
        host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .SingleOrDefault(e => e.RoutePattern.RawText == path);
}
