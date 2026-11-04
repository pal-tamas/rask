using System.Net;
using System.Text.RegularExpressions;

namespace Rask.Cli.E2E.Tests;

/// <summary>
///     The whole hot-reload loop, for real: scaffold an app, run it under <c>dotnet watch</c>, edit a
///     source file, and assert the running app applied the edit and told the browser about it.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a WebSocket client and not a browser.</b> The claim under test is "the edit reached the
///         open session without it being torn down". A live session <em>is</em> the WS connection: if the
///         server had restarted, the session id would be gone and the socket would be told
///         <c>{"type":"session","status":"unknown"}</c>. So holding the socket open across the edit and
///         receiving a frame with the new markup on it is a direct proof of exactly the thing a browser
///         test would infer indirectly from "no navigation happened" — with none of the browser's flake,
///         and it asserts the actual wire frames on the way past.
///     </para>
///     <para>
///         <b>Cost and gating.</b> This packs the repo's packages, restores, builds, and runs a watch
///         session, so it is behind its own <c>RASK_WATCH_E2E=1</c> switch rather than the CLI build gate —
///         folding it in would slow every run of that gate. It also needs
///         <c>RASK_CLI_BUILD_E2E=1</c>'s local feed, which it shares.
///     </para>
///     <para>
///         <b>Honesty about what is covered.</b> Editing a method body is the canonical hot-reload-supported
///         edit and is asserted strictly. Adding a type is a rude edit that restarts the process — asserted
///         as "the app comes back and serves the new code", not as an in-place apply, because it is not one.
///     </para>
/// </remarks>
[Collection(WatchHotReloadCollection.Name)]
public sealed class WatchHotReloadE2ETests
{
    private const string SkipReason =
        "Watch hot-reload gate: set RASK_WATCH_E2E=1 (and RASK_CLI_BUILD_E2E=1) to run it. It packs this " +
        "commit's packages, builds a generated app, and runs a real `dotnet watch` session.";

    private static bool Enabled =>
        Environment.GetEnvironmentVariable("RASK_WATCH_E2E") == "1" && CliBuildE2E.Enabled;

    /// <summary>
    ///     <b>The empty-delta mystery (#536) was a symlink in the project path.</b> For months every edit
    ///     here came back as <c>No managed code changes to apply</c>, and the two apply-dependent cases
    ///     below were gated off as "never observed green". The cause turned out to be one character of
    ///     path: <see cref="Path.GetTempPath" /> returns <c>/var/folders/…</c> on macOS and <c>/var</c> is
    ///     a symlink to <c>/private/var</c>. Hand <c>dotnet watch</c> a project path that traverses a
    ///     symlink and it computes an <b>empty</b> Edit-and-Continue delta — silently. Resolve the path
    ///     first (see <see cref="RealPath" />) and the identical edit applies.
    ///     <para>
    ///         It is worth being precise about how quiet the failure is, because that is what made it hard.
    ///         With <c>RASK_WATCH_E2E_VERBOSE=1</c> watch reports every step as healthy: the session
    ///         starts, the app launches with the delta applier and <c>DOTNET_MODIFIABLE_ASSEMBLIES=debug</c>,
    ///         the full capability set is negotiated, the change is seen (<c>File updated: …</c>), and the
    ///         workspace document is genuinely updated (<c>Updating document text of …</c>,
    ///         <c>Solution after document update: v2</c>). Only then does the update come back empty. There
    ///         is no error, no warning, and no hint that a path is involved. The tell — and the only one —
    ///         is that watch echoes the project path unresolved while the app reports its content root
    ///         resolved.
    ///     </para>
    ///     <para>
    ///         <b>Not Rask-specific, and not the test host.</b> A bare <c>dotnet new web</c> app reproduces
    ///         it exactly, and the same Rask app applies cleanly when only the path form changes. The
    ///         original theory — that watch cannot produce a delta as a grandchild of <c>dotnet test</c> —
    ///         was disproven separately; process ancestry was never the variable.
    ///     </para>
    ///     <para>
    ///         Ruled out along the way, each by experiment: port collisions; the launch profile overriding
    ///         <c>ASPNETCORE_URLS</c>; a stray <c>.tmp</c> sibling from an atomic write; a pre-build leaving
    ///         output newer than sources; a settle/timing race; a stale NuGet cache (that one was real, and
    ///         is fixed); the MSBuild/VSTest environment injected into every child; and a lazily-captured
    ///         baseline. The symlink was also "ruled out" once — wrongly. That attempt resolved the path
    ///         with <see cref="Path.GetFullPath(string)" />, which normalises separators and <c>..</c> but
    ///         never follows a symlink, so it changed nothing and looked like a negative result.
    ///     </para>
    /// </summary>

    [Fact]
    public async Task Editing_a_render_body_reaches_the_open_session_without_restarting_it()
    {
        Assert.SkipUnless(Enabled, SkipReason);

        await using var app = await WatchApp.StartAsync("WatchEdit");

        // The dev flag must be on the served HTML, or the client would ignore every dev frame.
        var html = await app.Http.GetStringAsync("/", TestContext.Current.CancellationToken);
        Assert.Contains("data-rask-dev", html, StringComparison.Ordinal);
        Assert.Contains(WatchApp.OriginalHeading, html, StringComparison.Ordinal);

        var sessionId = WatchApp.SessionId(html);
        using var socket = await app.ConnectAsync(sessionId);

        // Edit the heading inside HomePage.Render() — a statement change in an existing method body, the
        // edit C# Hot Reload is designed to apply.
        app.ReplaceInFile("Features/Home/HomePage.cs", WatchApp.OriginalHeading, WatchApp.EditedHeading);

        // Confirm the apply landed in the process before asserting on the socket. Separating the two
        // means a failure says which half broke — the reload not happening at all, or happening without
        // reaching the open session — instead of one indistinguishable timeout.
        var applied = await app.PollForContentAsync(
            "/", WatchApp.EditedHeading, TimeSpan.FromSeconds(90));
        Assert.True(applied, $"the edit never reached the running app at all.{app.Log}");

        var frames = await app.ReadFramesUntilAsync(
            socket,
            f => f.Contains(WatchApp.EditedHeading, StringComparison.Ordinal),
            TimeSpan.FromSeconds(60));

        var all = string.Join("\n", frames);

        // The session was never told it was unknown — i.e. the process did not restart underneath it.
        Assert.DoesNotContain("\"status\":\"unknown\"", all, StringComparison.Ordinal);
        Assert.Contains(WatchApp.EditedHeading, all, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_applied_hot_reload_is_announced_to_the_browser()
    {
        Assert.SkipUnless(Enabled, SkipReason);

        await using var app = await WatchApp.StartAsync("WatchPing");

        var sessionId = WatchApp.SessionId(await app.Http.GetStringAsync("/", TestContext.Current.CancellationToken));
        using var socket = await app.ConnectAsync(sessionId);

        app.ReplaceInFile("Features/Home/HomePage.cs", WatchApp.OriginalHeading, WatchApp.EditedHeading);

        var applied = await app.PollForContentAsync(
            "/", WatchApp.EditedHeading, TimeSpan.FromSeconds(90));
        Assert.True(applied, $"the edit never reached the running app at all.{app.Log}");

        var frames = await app.ReadFramesUntilAsync(
            socket,
            f => f.Contains("\"hotReload\"", StringComparison.Ordinal),
            TimeSpan.FromSeconds(60));

        // The exact literal the client branches on — pinned here end-to-end, over a real socket.
        Assert.Contains(
            frames,
            f => f.Contains("""{"type":"hotReload","status":"applied"}""", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Editing_a_route_template_serves_the_new_url()
    {
        Assert.SkipUnless(Enabled, SkipReason);

        // Routes are registered by a [ModuleInitializer], which the runtime never re-runs — before the
        // generated RefreshAll() existed this silently did nothing at all.
        //
        // Whether it applies in place or restarts is up to the runtime's attribute-update support, so this
        // asserts the outcome (the new URL works, the old is gone) rather than the mechanism. It also
        // exercises HotReloadAutoRestart: without it a restart would stop at an interactive prompt.
        await using var app = await WatchApp.StartAsync("WatchRoute");

        Assert.Contains(WatchApp.OriginalHeading, await app.Http.GetStringAsync("/", TestContext.Current.CancellationToken), StringComparison.Ordinal);

        app.ReplaceInFile("Features/Home/HomePage.cs", "[Route(\"/\")]", "[Route(\"/moved\")]");

        // Poll on the page's own content rather than the status code. A fallen-through path answers
        // 404 now, so a status check would no longer be meaningless here — but content is still the
        // stronger signal, because it proves this PAGE is being served rather than merely that
        // something is.
        var moved = await app.PollForContentAsync(
            "/moved", WatchApp.OriginalHeading, TimeSpan.FromSeconds(120));
        Assert.True(moved, $"'/moved' never started serving the home page.{app.Log}");

        var body = await app.Http.GetStringAsync("/moved", TestContext.Current.CancellationToken);
        // Replaced, not appended: re-running the generated registry with Add() would have registered the
        // page twice, and the router would render it twice under the one route.
        Assert.Single(Regex.Matches(body, Regex.Escape(WatchApp.OriginalHeading)));

        // And it really moved — the old path no longer serves the page, and now says so. Asserting the
        // status is the stronger claim, and it is only available because a fallen-through path answers
        // 404; while it answered 200 this had to be expressed as "the body lost the heading", which is
        // why GetStringAsync — which throws on a non-2xx — used to be fine here.
        var old = await app.Http.GetAsync("/", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, old.StatusCode);
        Assert.DoesNotContain(
            WatchApp.OriginalHeading,
            await old.Content.ReadAsStringAsync(TestContext.Current.CancellationToken),
            StringComparison.Ordinal);
    }

}

/// <summary>
///     Serialises the watch cases. Each one runs a real app on its own ephemeral port, and the port is
///     chosen a moment before the child binds it — run in parallel, two cases can be handed the same
///     freed port and the second dies with "address already in use". They are also memory-hungry
///     (MSBuild + a watch session + an ASP.NET host each).
/// </summary>
[CollectionDefinition(WatchHotReloadCollection.Name, DisableParallelization = true)]
public sealed class WatchHotReloadCollection
{
    public const string Name = "WatchHotReload";
}
