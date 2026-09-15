using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

namespace Rask.Spa.Hosting.Tests;

/// <summary>
/// #1073: a host serving its WebAssembly client maps the debug proxy the SDK's dev server maps for a standalone app,
/// so a browser debugger's <c>inspectUri</c> works against either.
/// </summary>
public sealed class SpaDebugProxyTests
{
    [Theory]
    [InlineData("ws://127.0.0.1:9222/devtools/browser/abc", "http://127.0.0.1:9222")]
    [InlineData("ws://localhost:51234/devtools/browser/abc", "http://localhost:51234")]
    [InlineData("ws://[::1]:9222/devtools/browser/abc", "http://[::1]:9222")]
    public void A_browser_on_this_machine_is_accepted(string value, string devTools)
    {
        Assert.True(SpaDebugProxy.TryParseBrowser(value, out var browser, out var origin));
        Assert.Equal(devTools, origin);
        Assert.Equal("/devtools/browser/abc", browser.AbsolutePath);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("http://127.0.0.1:9222/devtools/browser/abc")]
    [InlineData("ws://attacker.example:9222/devtools/browser/abc")]
    [InlineData("ws://10.0.0.5:9222/devtools/browser/abc")]
    public void Anything_but_a_loopback_devtools_socket_is_refused(string? value) =>
        // The proxy connects wherever the request says; on a host that is not this machine it would be a relay.
        Assert.False(SpaDebugProxy.TryParseBrowser(value, out _, out _));

    [Fact]
    public void The_debugger_is_redirected_to_the_proxy_with_the_browser_sockets_path() =>
        Assert.Equal(
            "ws://127.0.0.1:60442/devtools/browser/abc?x=1",
            SpaDebugProxy.RedirectTarget(new Uri("http://127.0.0.1:60442"), new Uri("ws://127.0.0.1:9222/devtools/browser/abc?x=1")));

    [Theory]
    [InlineData("info: Microsoft.Hosting.Lifetime[14]", null)]
    [InlineData("      Now listening on: http://127.0.0.1:60442", "http://127.0.0.1:60442/")]
    public void The_proxy_address_is_read_off_its_listening_line(string line, string? address) =>
        Assert.Equal(address, SpaDebugProxy.ListeningAddress(line)?.ToString());

    [Fact]
    public void The_proxy_path_file_sits_beside_the_build_manifest()
    {
        Assert.Equal("/c/bin/Debug/net10.0/Client.rask-debugproxy",
            SpaDebugProxy.PathFileFor("/c/bin/Debug/net10.0/Client.staticwebassets.runtime.json"));
        Assert.Null(SpaDebugProxy.PathFileFor("/c/something-else.json"));
    }

    [Fact]
    public void The_proxy_runs_on_loopback_owned_by_this_process_without_the_apps_environment()
    {
        var start = SpaDebugProxy.StartInfo("dotnet", "/p/BrowserDebugHost.dll", 42, "http://127.0.0.1:9222");

        Assert.Equal(["exec", "/p/BrowserDebugHost.dll", "--OwnerPid", "42", "--DevToolsUrl", "http://127.0.0.1:9222"], start.ArgumentList);
        Assert.Equal("http://127.0.0.1:0", start.Environment["ASPNETCORE_URLS"]);
        Assert.False(start.Environment.ContainsKey("ASPNETCORE_ENVIRONMENT"));
    }

    [Fact]
    public async Task A_refused_browser_is_a_400_and_starts_nothing()
    {
        await using var app = await HostAsync("/does/not/exist/BrowserDebugHost.dll");
        using var http = app.GetTestServer().CreateClient();

        var response = await http.GetAsync("/_framework/debug/ws-proxy?browser=ws%3A%2F%2Fattacker.example%3A9222%2Fdevtools%2Fbrowser%2Fabc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [SkippableFact]
    public async Task The_real_proxy_starts_and_the_debugger_is_redirected_to_it()
    {
        // The proxy the WebAssembly SDK ships, when this machine has restored it — as any machine that has built a
        // Rask WebAssembly project has.
        var host = FindBrowserDebugHost();
        Skip.If(host is null, "No BrowserDebugHost.dll in the NuGet cache (build any WebAssembly project to restore it).");

        await using var app = await HostAsync(host!);
        using var http = app.GetTestServer().CreateClient();

        var response = await http.GetAsync("/_framework/debug/ws-proxy?browser=ws%3A%2F%2F127.0.0.1%3A9222%2Fdevtools%2Fbrowser%2Fabc");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var location = response.Headers.Location!.ToString();
        Assert.Matches(@"^ws://127\.0\.0\.1:\d+/devtools/browser/abc$", location);
        Assert.DoesNotContain(":9222", location, StringComparison.Ordinal);
    }

    private static async Task<WebApplication> HostAsync(string hostDll)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        SpaDebugProxy.Map(app, "", hostDll);
        await app.StartAsync();
        return app;
    }

    private static string? FindBrowserDebugHost()
    {
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var pack = Path.Combine(packages, "microsoft.net.sdk.webassembly.pack");
        return Directory.Exists(pack)
            ? Directory.EnumerateFiles(pack, "BrowserDebugHost.dll", SearchOption.AllDirectories).Order(StringComparer.Ordinal).LastOrDefault()
            : null;
    }
}
