using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

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
///         folding it in would slow every pre-push that touches a generator. It also needs
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

    [SkippableFact]
    public async Task Editing_a_render_body_reaches_the_open_session_without_restarting_it()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var app = await WatchApp.StartAsync("WatchEdit");

        // The dev flag must be on the served HTML, or the client would ignore every dev frame.
        var html = await app.Http.GetStringAsync("/");
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

    [SkippableFact]
    public async Task An_applied_hot_reload_is_announced_to_the_browser()
    {
        Skip.IfNot(Enabled, SkipReason);

        await using var app = await WatchApp.StartAsync("WatchPing");

        var sessionId = WatchApp.SessionId(await app.Http.GetStringAsync("/"));
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

    [SkippableFact]
    public async Task Editing_a_route_template_serves_the_new_url()
    {
        Skip.IfNot(Enabled, SkipReason);

        // Routes are registered by a [ModuleInitializer], which the runtime never re-runs — before the
        // generated RefreshAll() existed this silently did nothing at all.
        //
        // Whether it applies in place or restarts is up to the runtime's attribute-update support, so this
        // asserts the outcome (the new URL works, the old is gone) rather than the mechanism. It also
        // exercises HotReloadAutoRestart: without it a restart would stop at an interactive prompt.
        await using var app = await WatchApp.StartAsync("WatchRoute");

        Assert.Contains(WatchApp.OriginalHeading, await app.Http.GetStringAsync("/"), StringComparison.Ordinal);

        app.ReplaceInFile("Features/Home/HomePage.cs", "Route => \"/\";", "Route => \"/moved\";");

        // Poll on the page's own content, never on the status code: an unrouted path falls through to
        // the framework's not-found page, which is served with 200 — so a status check here would pass
        // before the edit had landed and prove nothing.
        var moved = await app.PollForContentAsync(
            "/moved", WatchApp.OriginalHeading, TimeSpan.FromSeconds(120));
        Assert.True(moved, $"'/moved' never started serving the home page.{app.Log}");

        var body = await app.Http.GetStringAsync("/moved");
        // Replaced, not appended: re-running the generated registry with Add() would have registered the
        // page twice, and the router would render it twice under the one route.
        Assert.Single(Regex.Matches(body, Regex.Escape(WatchApp.OriginalHeading)));

        // And it really moved — the old path no longer serves the page.
        Assert.DoesNotContain(
            WatchApp.OriginalHeading, await app.Http.GetStringAsync("/"), StringComparison.Ordinal);
    }

    /// <summary>A scaffolded app running under a real <c>dotnet watch</c>, with its stdout drained.</summary>
    private sealed class WatchApp : IAsyncDisposable
    {
        // The scaffolded home page's own copy, edited in place. Deliberately NOT a marker this test
        // injects first: inserting an extra element before the build and then editing it was reported by
        // watch as "No managed code changes to apply", while editing text the template already ships
        // applies cleanly. If `rask new` ever changes this string, ReplaceInFile's assert names it.
        internal const string OriginalHeading = "Hello, Rask!";
        internal const string EditedHeading = "Hello, Edited!";

        private readonly StringBuilder _log = new();
        private readonly object _logLock = new();
        private Process _process = null!;
        private string _temp = null!;

        public HttpClient Http { get; private set; } = null!;
        public string ProjectDir { get; private set; } = null!;
        public int Port { get; private set; }

        public string Log
        {
            get { lock (_logLock) { return "\n--- dotnet watch output ---\n" + _log; } }
        }

        public static async Task<WatchApp> StartAsync(string name)
        {
            var (feed, version) = await CliBuildE2E.LocalFeed.Value;
            var app = new WatchApp
            {
                // RealPath.Resolve, not Path.Combine alone: on macOS Path.GetTempPath() is
                // /var/folders/… and /var is a symlink to /private/var. Handing `dotnet watch` a project
                // path that traverses a symlink makes it compute an EMPTY hot-reload delta — it reports
                // "File updated", updates its workspace document, then says "No managed code changes to
                // apply" and applies nothing, with no error anywhere. That single character of path
                // difference is what kept the two apply-dependent cases below red for months (#536).
                _temp = RealPath.Resolve(
                    Path.Combine(Path.GetTempPath(), "rask-watch-e2e", Guid.NewGuid().ToString("N")))
            };
            app.ProjectDir = Path.Combine(app._temp, name);

            var result = ProjectGenerator.GenerateServer(app.ProjectDir, name, new ServerBatteries(), version);
            var fs = new SystemFileSystem();
            foreach (var file in result.Files)
            {
                fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
                fs.WriteAllText(file.Path, file.Content);
            }

            CliBuildE2E.WriteNuGetConfig(fs, app.ProjectDir, feed);

            // Drop the scaffolded launch profile. Its applicationUrl (https://localhost:5001) takes
            // precedence over ASPNETCORE_URLS, so leaving it in place makes every run fight over one
            // fixed port instead of using the ephemeral one below — and its launchBrowser would open a
            // real browser mid-suite. ASPNETCORE_ENVIRONMENT is set explicitly in the child environment,
            // so nothing is lost by removing it.
            var launchSettings = Path.Combine(app.ProjectDir, "Properties", "launchSettings.json");
            if (File.Exists(launchSettings))
            {
                File.Delete(launchSettings);
            }

            var home = Path.Combine(app.ProjectDir, "Features", "Home", "HomePage.cs");
            Assert.Contains(OriginalHeading, File.ReadAllText(home), StringComparison.Ordinal);

            // Deliberately NOT pre-built: watch performs its own build and captures its Edit-and-Continue
            // baseline from it, so letting it do that keeps the baseline and the running process in step.
            // (An earlier note here blamed pre-building for the "No managed code changes to apply" failure.
            // That was a guess, and it was wrong — the cause was the unresolved temp path, resolved above.)
            var csproj = Path.Combine(app.ProjectDir, name + ".csproj");

            app.Port = FreePort();
            app.StartWatch(csproj);
            app.Http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{app.Port}") };

            try
            {
                var ready = await app.PollAsync("/", System.Net.HttpStatusCode.OK, TimeSpan.FromSeconds(180));
                Assert.True(ready, $"the watch app never started listening on {app.Port}.{app.Log}");

                // Serving a request is not the same as being ready to hot-reload: watch announces
                // "Hot reload enabled" once its EnC baseline is in place, and an edit that lands before
                // then is diffed against nothing and reported as "No managed code changes to apply".
                var armed = await app.WaitForLogAsync("Hot reload enabled", TimeSpan.FromSeconds(60));
                Assert.True(armed, $"watch never reported hot reload as enabled.{app.Log}");
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                return app;
            }
            catch
            {
                // The caller never receives the instance, so its `await using` cannot dispose it — and a
                // leaked `dotnet watch` keeps rebuilding for the rest of the session. Clean up here.
                await app.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }

        private void StartWatch(string csproj)
        {
            var psi = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = ProjectDir
            };
            // --no-launch-profile belongs to `run`, so it follows it. Belt and braces with deleting the
            // profile above: either alone would let ASPNETCORE_URLS win, both make it unambiguous.
            // RASK_WATCH_E2E_VERBOSE=1 turns on watch's own diagnosis — the launch command line, the
            // negotiated hot-reload capabilities, and what it decided about each change. It is the only way
            // to tell "the delta was empty" apart from "the plumbing never came up", which is exactly the
            // distinction the two apply-dependent cases keep running into.
            var argv = new List<string> { "watch", "--project", csproj, "--non-interactive" };
            if (Environment.GetEnvironmentVariable("RASK_WATCH_E2E_VERBOSE") == "1")
            {
                argv.Add("--verbose");
            }

            argv.AddRange(["run", "--no-launch-profile"]);

            foreach (var a in argv)
            {
                psi.ArgumentList.Add(a);
            }

            // The same environment `rask dev` builds, plus a fixed port. HotReloadAutoRestart is what stops
            // a rude edit stalling on watch's interactive prompt.
            psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            psi.Environment["ASPNETCORE_URLS"] = $"http://127.0.0.1:{Port}";
            psi.Environment["HotReloadAutoRestart"] = "true";
            psi.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";

            _process = Process.Start(psi)!;
            _ = DrainAsync(_process.StandardOutput);
            _ = DrainAsync(_process.StandardError);
        }

        private async Task DrainAsync(StreamReader reader)
        {
            try
            {
                while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line)
                {
                    lock (_logLock)
                    {
                        _log.AppendLine(line);
                    }
                }
            }
            catch (Exception)
            {
                // The process went away; the log has whatever it managed to say.
            }
        }

        public async Task<bool> PollAsync(string path, System.Net.HttpStatusCode expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    return false;
                }

                try
                {
                    using var response = await Http.GetAsync(path).ConfigureAwait(false);
                    if (response.StatusCode == expected)
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Not up yet, or mid-restart.
                }

                await Task.Delay(300).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>Polls the drained watch output until it contains <paramref name="marker" />.</summary>
        public async Task<bool> WaitForLogAsync(string marker, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                lock (_logLock)
                {
                    if (_log.ToString().Contains(marker, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }

                if (_process.HasExited)
                {
                    return false;
                }

                await Task.Delay(250).ConfigureAwait(false);
            }

            return false;
        }

        /// <summary>Polls until <paramref name="path" /> serves a body containing <paramref name="expected" />.</summary>
        public async Task<bool> PollForContentAsync(string path, string expected, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (_process.HasExited)
                {
                    return false;
                }

                try
                {
                    var body = await Http.GetStringAsync(path).ConfigureAwait(false);
                    if (body.Contains(expected, StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
                catch (Exception)
                {
                    // Not up yet, or mid-restart.
                }

                await Task.Delay(300).ConfigureAwait(false);
            }

            return false;
        }

        public async Task<ClientWebSocket> ConnectAsync(string sessionId)
        {
            var socket = new ClientWebSocket();
            await socket.ConnectAsync(new Uri($"ws://127.0.0.1:{Port}/rask/ws"), CancellationToken.None)
                .ConfigureAwait(false);

            var hello = Encoding.UTF8.GetBytes($$"""{"type":"hello","session":"{{sessionId}}"}""");
            await socket.SendAsync(hello, WebSocketMessageType.Text, true, CancellationToken.None)
                .ConfigureAwait(false);
            return socket;
        }

        /// <summary>Reads frames until one satisfies <paramref name="predicate" />, or the timeout elapses.</summary>
        public async Task<List<string>> ReadFramesUntilAsync(
            ClientWebSocket socket, Func<string, bool> predicate, TimeSpan timeout)
        {
            var frames = new List<string>();
            using var cts = new CancellationTokenSource(timeout);
            var buffer = new byte[64 * 1024];

            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var text = new StringBuilder();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(buffer, cts.Token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            return frames;
                        }

                        text.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));
                    }
                    while (!result.EndOfMessage);

                    frames.Add(text.ToString());
                    if (predicate(frames[^1]))
                    {
                        return frames;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Assert.Fail(
                    $"timed out waiting for the expected frame. Frames seen:\n{string.Join("\n", frames)}{Log}");
            }

            return frames;
        }

        /// <summary>
        ///     Rewrites a source file in place, the way saving from an editor does.
        /// </summary>
        /// <remarks>
        ///     Deliberately NOT a write-temp-then-rename: a <c>.tmp</c> sibling lands inside the watched
        ///     tree, and watch then reports <c>Files updated: HomePage.cs.tmp, HomePage.cs</c> and
        ///     concludes <c>No managed code changes to apply</c> — it has already refreshed its Roslyn
        ///     baseline from the first event, so the rename looks like a no-op and the edit is never
        ///     applied. Writing straight to the file is both simpler and what actually works.
        /// </remarks>
        public void ReplaceInFile(string relativePath, string oldText, string newText)
        {
            var path = Path.Combine(ProjectDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
            var content = File.ReadAllText(path);
            Assert.Contains(oldText, content, StringComparison.Ordinal);

            File.WriteAllText(path, content.Replace(oldText, newText, StringComparison.Ordinal));
        }

        public static string SessionId(string html)
        {
            var match = Regex.Match(html, "data-rask-root=\"([^\"]+)\"");
            Assert.True(match.Success, $"no data-rask-root in the served HTML:\n{html}");
            return match.Groups[1].Value;
        }

        private static int FreePort()
        {
            // Never a fixed port: the repo's other E2E suites hard-code theirs, and a concurrent worktree
            // running its own would silently hijack this one.
            var listener = new TcpListener(System.Net.IPAddress.Loopback, 0);
            listener.Start();
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        public async ValueTask DisposeAsync()
        {
            Http?.Dispose();
            try
            {
                if (_process is { HasExited: false })
                {
                    // Kill the tree: watch runs the app as a grandchild, and a leaked watch rebuilds forever.
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync(new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token)
                        .ConfigureAwait(false);
                }
            }
            catch (Exception)
            {
                // Already gone.
            }

            _process?.Dispose();
            CliBuildE2E.TryDeleteDirectory(_temp);
        }
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
