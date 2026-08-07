using System.Diagnostics;
using System.Text;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Runs <c>samples/Rask.Example.Wasm.Host</c> under a real <c>dotnet watch</c> with the WASM dev
///     bundle on, so an edit to a client component can be driven and observed in a real browser.
/// </summary>
/// <remarks>
///     <para>
///         <b>Why a source file is written into the sample.</b> Proving hot reload needs a component
///         whose rendered text actually changes, and the only honest way to change it is to edit a
///         <c>.cs</c> file that the running app compiled. Rather than mutate a tracked sample page —
///         which would race the other E2E fixtures reading the same tree, and leave a dirty diff on a
///         crash — this fixture writes its own single-purpose probe page, owns it for the run, and
///         deletes it afterwards. It is added <em>before</em> watch starts, so the initial build
///         includes it and the later edit is an ordinary method-body change rather than a rude edit.
///     </para>
///     <para>
///         If a hard kill ever leaves the file behind, it is untracked, obviously named, and removed by
///         the next run's <see cref="InitializeAsync" />.
///     </para>
/// </remarks>
public sealed class WasmWatchAppFixture : IAsyncLifetime
{
    internal const string OriginalMarker = "hot-reload-probe-original";
    internal const string EditedMarker = "hot-reload-probe-edited";
    internal const string ProbeRoute = "hot-reload-probe";

    private readonly Lock _logLock = new();
    private readonly StringBuilder _log = new();

    private Process? _process;
    private string _probeFile = string.Empty;

    // Assigned by the OS at InitializeAsync — see LoopbackPort. This fixture used to hold 5101, which it
    // shared with SiteWasmAppFixture in a different (parallel) collection until #612 moved that one to an
    // ephemeral port; the collision was removed by the other side of it, not by this one.
    private int _port;

    public string BaseUrl => $"http://localhost:{_port}";

    public string ServerLog
    {
        get
        {
            lock (_logLock)
            {
                return _log.ToString();
            }
        }
    }

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepoRoot();
        var host = Path.Combine(repoRoot, "samples", "Rask.Example.Wasm.Host");
        _probeFile = Path.Combine(repoRoot, "samples", "Rask.Example.Wasm", "Features", "HotReloadProbePage.cs");

        // An OS-assigned port, rather than testing against whatever is on a fixed one. A leftover host
        // from an earlier run answers the readiness poll perfectly happily, and every later assertion
        // then measures a server that never saw the edit — which reads as "hot reload is broken" and is
        // not. The old guard probed IPv4 loopback for a fixed number and told you to `pkill`;
        // LoopbackPort.Reserve asks for one nobody holds, on the family `localhost` resolves to.
        _port = LoopbackPort.Reserve();

        WriteProbe(OriginalMarker);

        // Pre-build with exactly the properties watch will use. Two reasons, and the second is not
        // optional: it keeps the first in-session build incremental (a cold WASM build inside watch is
        // minutes), and — because pinning the version rewrites every generated AssemblyInfo.cs — doing
        // it here means those rewrites land BEFORE watch is watching. Leave them to the first
        // in-session build and watch sees a burst of file changes, restarts the app it has only just
        // started, and the restart collides with the old instance on the fixed port.
        await BuildAsync(host);

        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = host,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("watch");
        // --non-interactive: watch's rude-edit prompt has no terminal to ask here and would block.
        psi.ArgumentList.Add("--non-interactive");
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--no-launch-profile");
        // The whole point: serve the client's BUILD output. Without it the host serves the trimmed
        // published bundle, where MetadataUpdater.IsSupported is false and no delta can ever apply.
        psi.ArgumentList.Add("--property:RaskWasmDevBundle=true");

        // Pins the assembly version for the session. Without it every Edit-and-Continue recompile is
        // rejected outright:
        //
        //   error CS7038: Failed to emit module 'Rask.Example.Wasm': Changing the version of an
        //   assembly reference is not allowed during debugging: 'Rask.Bootstrap, Version=0.0.0.0'
        //   changed version to '1.0.0.0'.
        //
        // The repo versions its assemblies with MinVer, so a normal build stamps 0.0.0.0 while the
        // evaluation behind the EnC recompile falls back to the SDK default 1.0.0.0 — and Roslyn will
        // not apply a delta across an assembly-version change. It only bites the in-repo samples,
        // which reach the framework through ProjectReferences; a `rask new` app consumes the packages
        // and has no MinVer in its graph. So this is a harness concession, not a product workaround.
        psi.ArgumentList.Add("--property:MinVerSkip=true");

        psi.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;
        psi.Environment["DOTNET_WATCH_SUPPRESS_EMOJIS"] = "1";
        // Rude edits must restart rather than sit on a prompt.
        psi.Environment["HotReloadAutoRestart"] = "true";

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException("Failed to start `dotnet watch` for the WASM host.");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput));
        _ = Task.Run(() => DrainAsync(_process.StandardError));

        await WaitForReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is { HasExited: false })
        {
            try
            {
                _process.Kill(entireProcessTree: true);
                await _process.WaitForExitAsync();
            }
            catch (InvalidOperationException)
            {
                // Already gone.
            }
        }

        _process?.Dispose();

        try
        {
            if (File.Exists(_probeFile))
            {
                File.Delete(_probeFile);
            }
        }
        catch (IOException)
        {
            // Best effort — the file is untracked and the next run overwrites it.
        }
    }

    /// <summary>
    ///     Rewrites the probe page's rendered text. An in-place whole-file write, exactly like an editor
    ///     save, so watch sees the same change shape a developer would produce.
    /// </summary>
    public void EditProbe(string marker) => WriteProbe(marker);

    private void WriteProbe(string marker)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_probeFile)!);
        File.WriteAllText(
            _probeFile,
            $$"""
              using Rask.Core.Routing;
              using Rask.Example.Shared;

              namespace Rask.Example.Wasm.Features;

              // Written by WasmWatchAppFixture for the WASM hot-reload E2E and deleted afterwards.
              // Not part of the sample — do not commit.
              [Route("{{ProbeRoute}}")]
              [ParentRoute(typeof(ShowcaseLayout))]
              public sealed class HotReloadProbePage : Component
              {
                  protected override Component? Render() =>
                      H1(Id: "probe")["{{marker}}"];
              }

              """);
    }

    private static async Task BuildAsync(string hostDirectory)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = hostDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        psi.ArgumentList.Add("build");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add("Debug");
        psi.ArgumentList.Add("-m:1");
        psi.ArgumentList.Add("--property:RaskWasmDevBundle=true");
        psi.ArgumentList.Add("--property:MinVerSkip=true");

        using var build = Process.Start(psi)
                          ?? throw new InvalidOperationException("Failed to pre-build the WASM host.");

        var output = await build.StandardOutput.ReadToEndAsync();
        var error = await build.StandardError.ReadToEndAsync();
        await build.WaitForExitAsync();

        if (build.ExitCode != 0)
        {
            throw new InvalidOperationException($"Pre-build of the WASM host failed.\n{output}\n{error}");
        }
    }

    private async Task WaitForReadyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        // Generous: the first build compiles the client project too (no nested publish, but still a
        // full WASM build) before the host starts listening.
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(5);

        while (DateTime.UtcNow < deadline)
        {
            if (_process is { HasExited: true })
            {
                throw new InvalidOperationException($"`dotnet watch` exited before serving.\n{ServerLog}");
            }

            try
            {
                var response = await http.GetAsync(BaseUrl + "/");
                if (response.IsSuccessStatusCode)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                // Not listening yet.
            }
            catch (TaskCanceledException)
            {
            }

            await Task.Delay(500);
        }

        throw new InvalidOperationException($"WASM watch host never served {BaseUrl}.\n{ServerLog}");
    }

    private async Task DrainAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_logLock)
            {
                _log.AppendLine(line);
            }
        }
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
               ?? throw new InvalidOperationException($"Could not locate Rask.slnx from {AppContext.BaseDirectory}");
    }
}
