using System.Diagnostics;
using System.Text;

namespace Rask.Examples.E2E.Tests.Infrastructure;

public abstract class ExampleAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private readonly Lock _logLock = new();
    private readonly StringBuilder _stderr = new();
    private readonly StringBuilder _stdout = new();
    private Process? _process;

    protected abstract string ProjectRelativePath { get; }
    protected abstract int Port { get; }
    protected virtual TimeSpan ReadyTimeout => TimeSpan.FromSeconds(120);

    // When true the host is `dotnet publish`-ed and the published DLL is run from its own folder,
    // instead of `dotnet run --no-build`. Publishing gives the app production static-asset serving via
    // MapStaticAssets — package _content/* assets (e.g. Rask.Bootstrap's CSS) are fingerprinted,
    // brotli-pre-compressed and ETag/304-revalidated. `dotnet run` instead uses the dev static-asset
    // handler, which serves those assets uncompressed and `no-cache` without honouring conditional
    // requests, so on a throttled link the full Bootstrap CSS re-downloads on every navigation and
    // blows the slow-3G timeout. Hosts that exercise the slow-network journey opt in so the E2E
    // mirrors a real deployment.
    protected virtual bool RunPublished => false;

    // Extra environment variables for the spawned host process (e.g. config that production-mode
    // hosts now fail-fast without). Keys use the ASP.NET `__` config delimiter (e.g. "Jwt__Key").
    protected virtual IReadOnlyDictionary<string, string>? ExtraEnvironment => null;

    public string BaseUrl => $"http://localhost:{Port}";

    public string ServerLog
    {
        get
        {
            lock (_logLock)
            {
                return $"{_stdout}\n--- STDERR ---\n{_stderr}";
            }
        }
    }

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepoRoot();
        var projectPath = Path.Combine(repoRoot, ProjectRelativePath);

        var psi = RunPublished
            ? PublishAndBuildStartInfo(repoRoot, projectPath)
            : DotnetRunStartInfo(repoRoot, projectPath);
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";

        if (ExtraEnvironment is { } extra)
        {
            foreach (var (key, value) in extra)
            {
                psi.Environment[key] = value;
            }
        }

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException($"Failed to start `dotnet run` for {ProjectRelativePath}");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, _stdout));
        _ = Task.Run(() => DrainAsync(_process.StandardError, _stderr));

        await WaitForReadyAsync();
    }

    public async Task DisposeAsync()
    {
        if (_process is null)
        {
            return;
        }

        if (!_process.HasExited)
        {
            try { _process.Kill(true); }
            catch
            {
                /* race: already exited */
            }
        }

        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(cts.Token);
        }
        catch
        {
            /* timed out — best effort */
        }

        _process.Dispose();
    }

    private async Task WaitForReadyAsync()
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + ReadyTimeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"{ProjectRelativePath} exited before becoming ready (code {_process.ExitCode}).\n{ServerLog}");
            }

            try
            {
                using var resp = await http.GetAsync(BaseUrl);
                if ((int)resp.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                /* not yet listening */
            }
            catch (TaskCanceledException)
            {
                /* per-request timeout */
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"{ProjectRelativePath} did not respond on {BaseUrl} within {ReadyTimeout}.\n{ServerLog}");
    }

    private async Task DrainAsync(StreamReader reader, StringBuilder buffer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_logLock)
            {
                buffer.AppendLine(line);
            }
        }
    }

    // Returns a ProcessStartInfo that runs the *published* host. Prefers the folder CI published once in
    // the `e2e-build` job; if that isn't present (local dev), publishes the host on demand into a temp
    // folder first. Either way the DLL is launched from its publish folder — see RunPublishedDllStartInfo.
    private ProcessStartInfo PublishAndBuildStartInfo(string repoRoot, string projectPath)
    {
        var appName = Path.GetFileName(ProjectRelativePath.TrimEnd('/', '\\'));

        // CI publishes each publish-based host once in the `e2e-build` job (default output folder), ships
        // it in the shared build artifact, and every shard boots that prebuilt DLL — so the shards don't
        // each repeat a from-scratch restore+compile+publish. Prefer it when present; multiple fixtures
        // (e.g. Server + NativeServerSmoke) share the one folder and just launch it on their own ports.
        var prebuiltDir = Path.Combine(projectPath, "bin", Configuration, "net10.0", "publish");
        if (File.Exists(Path.Combine(prebuiltDir, $"{appName}.dll")))
        {
            return RunPublishedDllStartInfo(prebuiltDir, appName);
        }

        // Local-dev fallback: no CI prebuild present, so publish the host on demand into a temp folder.
        var publishDir = Path.Combine(Path.GetTempPath(), "rask-e2e-publish", $"{appName}-{Port}");
        Directory.CreateDirectory(publishDir);

        var publish = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", projectPath, "-c", Configuration, "-o", publishDir, "--nologo" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };

        using (var p = Process.Start(publish)
                       ?? throw new InvalidOperationException($"Failed to start `dotnet publish` for {ProjectRelativePath}"))
        {
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"`dotnet publish` failed for {ProjectRelativePath} (exit {p.ExitCode}).\n{stdout}\n{stderr}");
            }
        }

        return RunPublishedDllStartInfo(publishDir, appName);
    }

    // Runs the published host DLL *from its own folder* — the working directory becomes the content root,
    // so MapStaticAssets resolves the published wwwroot/_content assets. Running it from anywhere else
    // would point the content root at the wrong place and the asset endpoints would serve empty bodies.
    private ProcessStartInfo RunPublishedDllStartInfo(string publishDir, string appName) => new("dotnet")
    {
        ArgumentList = { Path.Combine(publishDir, $"{appName}.dll"), "--urls", BaseUrl },
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        WorkingDirectory = publishDir
    };

    // Plain `dotnet run --no-build` launch — serves a host the main test-suite build already built.
    // (For WASM hosts that build must pass -p:WasmBuildNative=false so the prebuilt .NET-WASM runtime
    // is present; the native relink is flaky in some build environments.) Building in-fixture would add
    // a concurrent MSBuild under the full parallel suite and crash a worker node, so fixtures stay
    // build-free.
    private ProcessStartInfo DotnetRunStartInfo(string repoRoot, string projectPath)
    {
        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };

        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(projectPath);
        psi.ArgumentList.Add("--no-launch-profile");
        psi.ArgumentList.Add("--no-build");
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(Configuration);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--urls");
        psi.ArgumentList.Add(BaseUrl);
        return psi;
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.slnx")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"Could not locate Rask.slnx walking up from {AppContext.BaseDirectory}");
    }
}
