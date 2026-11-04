using System.Diagnostics;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.RegularExpressions;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;
using Rask.Hosting.Shared;

namespace Rask.Cli.E2E.Tests;

/// <summary>A scaffolded app running under a real <c>dotnet watch</c>, with its stdout drained.</summary>
internal sealed class WatchApp : IAsyncDisposable
{
    // The scaffolded home page's own copy, edited in place. Deliberately NOT a marker this test
    // injects first: inserting an extra element before the build and then editing it was reported by
    // watch as "No managed code changes to apply", while editing text the template already ships
    // applies cleanly. If `rask new` ever changes this string, ReplaceInFile's assert names it.
    internal const string OriginalHeading = "Hello, Rask!";
    internal const string EditedHeading = "Hello, Edited!";

    /// <summary>What <c>dotnet watch</c> prints when the build failed and it is waiting for an edit to retry.</summary>
    private const string WatchBuildFailed = "Fix the error to continue";

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

    /// <summary>
    ///     The island dev server <c>rask dev</c> starts beside the app, or null when the app was started
    ///     without islands.
    /// </summary>
    public IslandViteServer? Islands { get; private set; }

    public static async Task<WatchApp> StartAsync(string name)
    {
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var app = Create(name);
        app.Write(ProjectGenerator.GenerateServer(app.ProjectDir, name, new ServerBatteries(), version), feed);

        var home = Path.Combine(app.ProjectDir, "Features", "Home", "HomePage.cs");
        Assert.Contains(OriginalHeading, File.ReadAllText(home), StringComparison.Ordinal);

        // Deliberately NOT pre-built: watch performs its own build and captures its Edit-and-Continue
        // baseline from it, so letting it do that keeps the baseline and the running process in step.
        // (An earlier note here blamed pre-building for the "No managed code changes to apply" failure.
        // That was a guess, and it was wrong — the cause was the unresolved temp path, resolved above.)
        var csproj = Path.Combine(app.ProjectDir, name + ".csproj");

        app.Port = FreePort();
        app.StartWatch(WatchArguments(csproj), WatchEnvironment(app.Port));
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

    /// <summary>
    ///     A <paramref name="template" /> app scaffolded with <c>--islands react</c>, run the way <c>rask dev</c>
    ///     runs it: the same project detection, the same <c>dotnet watch</c> command line and environment, and
    ///     the island Vite dev server started from the pointer the dev-session build drops.
    /// </summary>
    /// <remarks>
    ///     Nothing here re-states what <c>rask dev</c> does: <see cref="DevTarget.Detect" /> reads the port the
    ///     way the CLI does, <see cref="DotnetWatchInvocation" /> builds the command line and environment
    ///     (<c>RaskDevSession=true</c>, <c>RASK_ISLANDS_DEV</c>), and <see cref="IslandViteServer" /> runs
    ///     <c>IslandDevCommand</c> over <c>IslandDevPointer</c>. If any of those drift, this drifts with them,
    ///     which is the point. The island port is a free one per run, written into the csproj the way an app
    ///     that moved off 5174 does: the dev server is <c>strictPort</c>, so a fixed port would make two
    ///     concurrent runs fail each other.
    /// </remarks>
    public static async Task<WatchApp> StartWithIslandAsync(string name, IslandHost template)
    {
        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var app = Create(name);

        var scaffold = template == IslandHost.Server
            ? ProjectGenerator.GenerateServer(app.ProjectDir, name, new ServerBatteries(), version, islands: ["react"])
            : ProjectGenerator.GenerateWasm(app.ProjectDir, name, pwa: false, docker: false, version, islands: ["react"]);

        // `rask new` chooses the island packages' versions; a package.json of "latest"s is not what it writes.
        var (pinned, unchosen) = await FrontEndVersions.PinAsync(scaffold, NpmRegistry.Public, CancellationToken.None);
        Assert.True(unchosen is null, $"the island packages' versions could not be chosen: {unchosen}");
        app.Write(pinned, feed);

        var csproj = Path.Combine(app.ProjectDir, name + ".csproj");
        var islandPort = FreePort();
        SetIslandDevServerPort(csproj, islandPort);

        var target = DevTarget.Detect(new SystemFileSystem(), app.ProjectDir, csproj);
        Assert.NotNull(target);
        Assert.True(target.HasIslands, $"rask dev would not start an island dev server for this {template} app.");
        Assert.Equal($"http://localhost:{islandPort}", target.IslandDevServerUrl);

        app.Port = FreePort();
        var arguments = DotnetWatchInvocation.BuildDotnetArguments(
            target.ProjectPath, once: false, noHotReload: false, launchProfile: null, nonInteractive: true,
            passthrough: [], target.Kind);
        var environment = DotnetWatchInvocation.Overlay(
            DotnetWatchInvocation.BuildEnvironment(
                target.Kind, restartOnRudeEdit: true, urls: $"http://127.0.0.1:{app.Port}",
                Environment.GetEnvironmentVariable, target.IslandDevServerUrl),
            [new("DOTNET_WATCH_SUPPRESS_EMOJIS", "1"), new("NO_COLOR", "1")]);

        app.StartWatch(arguments, environment);
        app.Http = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{app.Port}") };

        try
        {
            // What `rask dev` does beside the watch: wait for the dev-session build to name the config, then
            // serve it. The first build installs the island packages, so this is the slow half.
            // A failed build leaves watch waiting for a fix and never writes the pointer, so stop waiting then.
            using var failed = new CancellationTokenSource();
            using var started = new CancellationTokenSource();
            var watcher = app.CancelOnBuildFailureAsync(failed, started.Token);
            app.Islands = await IslandViteServer.StartAsync(
                target.ProjectDirectory, TimeSpan.FromMinutes(6), failed.Token);
            await started.CancelAsync();
            await watcher;
            Assert.True(
                app.Islands is not null,
                $"the dev-session build never wrote {IslandDevPointer.PathFor(target.ProjectDirectory)}, so rask dev "
                + $"would start no island dev server.{app.Log}");

            var ready = await app.PollAsync("/", System.Net.HttpStatusCode.OK, TimeSpan.FromSeconds(300));
            Assert.True(ready, $"the watch app never started listening on {app.Port}.{app.Log}{app.Islands.Log}");
            return app;
        }
        catch
        {
            await app.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Cancels <paramref name="failed" /> once watch reports a failed build, until <paramref name="stop" />.</summary>
    private async Task CancelOnBuildFailureAsync(CancellationTokenSource failed, CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            lock (_logLock)
            {
                if (_log.ToString().Contains(WatchBuildFailed, StringComparison.Ordinal))
                {
                    failed.Cancel();
                    return;
                }
            }

            try
            {
                await Task.Delay(250, stop).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static WatchApp Create(string name)
    {
        var app = new WatchApp
        {
            // RealPath.Resolve, not Path.Combine alone: on macOS Path.GetTempPath() is
            // /var/folders/… and /var is a symlink to /private/var. Handing `dotnet watch` a project
            // path that traverses a symlink makes it compute an EMPTY hot-reload delta — it reports
            // "File updated", updates its workspace document, then says "No managed code changes to
            // apply" and applies nothing, with no error anywhere. That single character of path
            // difference is what kept the two apply-dependent cases red for months (#536).
            _temp = RealPath.Resolve(
                Path.Combine(Path.GetTempPath(), "rask-watch-e2e", Guid.NewGuid().ToString("N")))
        };
        app.ProjectDir = Path.Combine(app._temp, name);
        return app;
    }

    /// <summary>Writes the scaffold, points it at the local feed, and drops its launch profile.</summary>
    private void Write(ScaffoldResult result, string feed)
    {
        var fs = new SystemFileSystem();
        foreach (var file in result.Files)
        {
            fs.CreateDirectory(Path.GetDirectoryName(file.Path)!);
            if (file.Bytes is { } bytes)
            {
                File.WriteAllBytes(file.Path, bytes);
            }
            else
            {
                fs.WriteAllText(file.Path, file.Content);
            }
        }

        CliBuildE2E.WriteNuGetConfig(fs, ProjectDir, feed);

        // Drop the scaffolded launch profile. Its applicationUrl (https://localhost:5001) takes
        // precedence over ASPNETCORE_URLS, so leaving it in place makes every run fight over one
        // fixed port instead of using the ephemeral one below — and its launchBrowser would open a
        // real browser mid-suite. ASPNETCORE_ENVIRONMENT is set explicitly in the child environment,
        // so nothing is lost by removing it.
        var launchSettings = Path.Combine(ProjectDir, "Properties", "launchSettings.json");
        if (File.Exists(launchSettings))
        {
            File.Delete(launchSettings);
        }
    }

    /// <summary>Moves the island dev server off 5174, the way an app author does: in the csproj.</summary>
    private static void SetIslandDevServerPort(string csproj, int port)
    {
        const string group = "<PropertyGroup>";
        var text = File.ReadAllText(csproj);
        var at = text.IndexOf(group, StringComparison.Ordinal);
        Assert.True(at >= 0, $"no {group} in {csproj}");

        File.WriteAllText(
            csproj,
            text.Insert(at + group.Length, $"\n    <RaskExternalDevServerPort>{port}</RaskExternalDevServerPort>"));
    }

    private static List<string> WatchArguments(string csproj) =>
        // --no-launch-profile belongs to `run`, so it follows it. Belt and braces with deleting the
        // profile: either alone would let ASPNETCORE_URLS win, both make it unambiguous.
        ["watch", "--project", csproj, "--non-interactive", "run", "--no-launch-profile"];

    private static Dictionary<string, string> WatchEnvironment(int port) => new(StringComparer.Ordinal)
    {
        // The same environment `rask dev` builds, plus a fixed port. HotReloadAutoRestart is what stops
        // a rude edit stalling on watch's interactive prompt.
        ["ASPNETCORE_ENVIRONMENT"] = "Development",
        ["ASPNETCORE_URLS"] = $"http://127.0.0.1:{port}",
        ["HotReloadAutoRestart"] = "true",
        ["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1",
    };

    private void StartWatch(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string> environment)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = ProjectDir
        };

        // RASK_WATCH_E2E_VERBOSE=1 turns on watch's own diagnosis — the launch command line, the
        // negotiated hot-reload capabilities, and what it decided about each change. It is the only way
        // to tell "the delta was empty" apart from "the plumbing never came up", which is exactly the
        // distinction the apply-dependent cases keep running into. A watch option, so it goes before `run`.
        var verbose = Environment.GetEnvironmentVariable("RASK_WATCH_E2E_VERBOSE") == "1";
        foreach (var argument in arguments)
        {
            if (verbose && argument == "run")
            {
                psi.ArgumentList.Add("--verbose");
            }

            psi.ArgumentList.Add(argument);
        }

        foreach (var (key, value) in environment)
        {
            psi.Environment[key] = value;
        }

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
                $"timed out waiting for the expected frame. Frames seen:\n{string.Join("\n", frames)}{Log}{Islands?.Log}");
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
        if (Islands is not null)
        {
            await Islands.DisposeAsync().ConfigureAwait(false);
        }

        CliBuildE2E.TryDeleteDirectory(_temp);
    }
}
