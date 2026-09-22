using Rask.Core;
using Rask.Core.Components;
using Rask.Core.Routing;
using Rask.Server.Tests.Infrastructure;

#pragma warning disable RASK014 // test-defined Component subclasses have no generated factories
#pragma warning disable RASK019 // test-infra app fills <head> inline

namespace Rask.Server.Tests.Endpoints;

/// <summary>
///     End-to-end: the server host auto-injects the runtime <c>&lt;script&gt;</c> at the end of
///     <c>&lt;body&gt;</c> on first paint, without the app declaring <c>RaskRuntimeScript()</c>.
/// </summary>
// Runs in the non-parallel "ScopedAssets" collection: A_path_base_prefixes_the_injected_runtime_script_src
// asserts on the host's pathBase, which the runtime script reads from the process-wide
// LiveOptions.PathBase static at render time. Another host configured concurrently (e.g.
// PathBaseEndpointTests, also in this collection) would clobber that static mid-render and
// flip the assertion. DisableParallelization on the collection serialises them.
[Collection("ScopedAssets")]
public sealed class RuntimeScriptInjectionTests
{
    [Fact]
    public async Task The_first_paint_injects_the_runtime_script_before_the_body_closes()
    {
        using var host = RaskTestHost.Create<ShellApp>();

        var body = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();

        Assert.Contains("<script src=\"/rask/rask.js\"></script></body>", body);
    }

    [Fact]
    public async Task A_path_base_prefixes_the_injected_runtime_script_src()
    {
        using var host = RaskTestHost.Create<ShellApp>(pathBase: "/appA");

        var body = await (await host.Http.GetAsync("/appA/")).Content.ReadAsStringAsync();

        Assert.Contains("<script src=\"/appA/rask/rask.js\"></script></body>", body);
    }

    [Fact]
    public async Task A_legacy_runtime_script_in_the_tree_still_emits_exactly_one()
    {
        using var host = RaskTestHost.Create<LegacyShellApp>();

        var body = await (await host.Http.GetAsync("/")).Content.ReadAsStringAsync();

        var marker = "src=\"/rask/rask.js\"";
        var first = body.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(first >= 0);
        Assert.Equal(-1, body.IndexOf(marker, first + marker.Length, StringComparison.Ordinal));
    }

    // No RaskRuntimeScript() — the framework injects it.
    private sealed class ShellApp(RouteState routeState) : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => new H1()[$"path={routeState.Path}"];
    }

    // Legacy tree that still contains the (now no-op) RaskRuntimeScript().
    private sealed class LegacyShellApp(RouteState routeState) : Component
    {
        protected override string? HtmlLang => null;

        protected override Component? Render() => [new H1()[$"path={routeState.Path}"], RaskRuntimeScript];
    }
}
