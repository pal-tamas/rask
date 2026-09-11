using System.Net;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Rask.Core.Diagnostics.DevTools;
using Rask.DevTools.Endpoints;
using Rask.Server.DevTools;
using Rask.Server.Tests.Infrastructure;

namespace Rask.DevTools.Tests;

/// <summary>
///     Who may open the devtools panel, through the real <c>AddRask</c> / <c>UseRask</c> page handler.
/// </summary>
/// <remarks>
///     <para>
///         The panel shows another session's state live, so every refusal here is a security property, not a nicety:
///         Development only, this machine only, the token the inspected page was rendered with, and the identity that
///         owns that session.
///     </para>
///     <para>
///         The test server reports no client address, and the admission check treats an unknown address as remote. So
///         the host under test marks its clients as loopback with a middleware — and the one test about remote clients
///         marks them as a LAN address instead.
///     </para>
/// </remarks>
public sealed partial class DevToolsPanelAdmissionTests
{
    [Fact]
    public async Task A_page_opened_from_its_own_tag_is_served()
    {
        using var host = Host("Development", IPAddress.Loopback);
        var panel = await PanelUrlFromPage(host);

        var response = await host.Http.GetAsync(panel);

        response.EnsureSuccessStatusCode();
        Assert.Contains("Inspecting session", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_panel_page_loads_no_devtools_of_its_own()
    {
        using var host = Host("Development", IPAddress.Loopback);
        var panel = await PanelUrlFromPage(host);

        var html = await host.Http.GetStringAsync(panel);

        Assert.DoesNotContain(DevToolsServerEndpoints.HostScriptPath, html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_wrong_or_missing_token_is_answered_as_not_found()
    {
        using var host = Host("Development", IPAddress.Loopback);
        var panel = await PanelUrlFromPage(host);
        var sessionOnly = panel[..panel.IndexOf("&t=", StringComparison.Ordinal)];

        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(sessionOnly)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(sessionOnly + "&t=AAAA")).StatusCode);
    }

    [Fact]
    public async Task A_token_for_another_session_is_answered_as_not_found()
    {
        using var host = Host("Development", IPAddress.Loopback);
        var first = await PanelUrlFromPage(host);
        var second = await PanelUrlFromPage(host);
        var tokenOfSecond = second[(second.IndexOf("&t=", StringComparison.Ordinal) + 3)..];

        var crossed = first[..(first.IndexOf("&t=", StringComparison.Ordinal) + 3)] + tokenOfSecond;

        Assert.Equal(HttpStatusCode.NotFound, (await host.Http.GetAsync(crossed)).StatusCode);
    }

    [Fact]
    public async Task A_remote_client_is_refused()
    {
        using var host = Host("Development", IPAddress.Parse("192.168.1.20"));
        var panel = await PanelUrlFromPage(host);

        Assert.Equal(HttpStatusCode.Forbidden, (await host.Http.GetAsync(panel)).StatusCode);
    }

    [Fact]
    public async Task Outside_development_the_panel_does_not_exist()
    {
        using var host = Host("Production", IPAddress.Loopback);

        var response = await host.Http.GetAsync(DevToolsServerEndpoints.Prefix + "/?inspect=x&t=y");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public void A_panel_session_is_never_rebuilt_from_a_resume_record()
    {
        using var host = Host("Development", IPAddress.Loopback);
        var devTools = host.Services.GetRequiredService<IRaskServerDevTools>();

        Assert.False(devTools.CanResume(DevToolsServerEndpoints.Prefix + "/"));
        Assert.False(devTools.CanResume(DevToolsServerEndpoints.Prefix));
        Assert.True(devTools.CanResume("/"));
        Assert.True(devTools.CanResume(DevToolsServerEndpoints.Prefix + "x"));
    }

    private static RaskTestHost Host(string environment, IPAddress client) =>
        RaskTestHost.Create<DevToolsTestApp>(
            configureServices: s => RaskDevToolsLoader.Attach(s),
            configureMiddleware: app => app.Use((ctx, next) =>
            {
                ctx.Connection.RemoteIpAddress = client;
                return next(ctx);
            }),
            environment: environment);

    /// <summary>Renders the app's page and returns the panel URL its devtools tag names, decoded.</summary>
    private static async Task<string> PanelUrlFromPage(RaskTestHost host)
    {
        var html = await host.Http.GetStringAsync("/");
        var match = DataPanel().Match(html);
        Assert.True(match.Success, "the page names no devtools panel:" + Environment.NewLine + html);
        return WebUtility.HtmlDecode(match.Groups[1].Value);
    }

    [GeneratedRegex("data-panel=\"([^\"]+)\"")]
    private static partial Regex DataPanel();
}
