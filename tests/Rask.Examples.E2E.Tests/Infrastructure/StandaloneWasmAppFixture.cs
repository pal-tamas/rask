using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Boots <c>Rask.Example.Wasm</c> standalone via WasmAppHost (the dev launcher that
///     `dotnet run` invokes for browser-wasm projects). WasmAppHost picks a random port
///     and prints <c>App url: http://127.0.0.1:{port}/index.html</c> — we parse that
///     from stdout instead of forcing a fixed --urls (WasmAppHost ignores ASP.NET
///     hosting args).
///     Note: WasmAppHost does NOT install a SPA fallback. Deep-link reloads at
///     /events etc. return 404. Tests that exercise this fixture must always start at
///     the root and navigate via sidebar buttons.
/// </summary>
public sealed class StandaloneWasmAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    private const string ProjectRelativePath = "samples/Rask.Example.Wasm";

    private static readonly Regex AppUrlPattern =
        new(@"App url:\s*(http://[^\s/]+)/index\.html", RegexOptions.Compiled);

    private readonly TaskCompletionSource<string> _baseUrl = new();
    private readonly Lock _logLock = new();
    private readonly StringBuilder _stderr = new();
    private readonly StringBuilder _stdout = new();
    private Process? _process;

    private TimeSpan ReadyTimeout { get; } = TimeSpan.FromSeconds(180);

    public string BaseUrl => _baseUrl.Task.IsCompletedSuccessfully
        ? _baseUrl.Task.Result
        : throw new InvalidOperationException(
            "BaseUrl read before WasmAppHost emitted 'App url:' line. Did InitializeAsync complete?");

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

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "run",
                "--project",
                projectPath,
                "--no-launch-profile",
                "--no-build",
                "-c",
                Configuration
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException($"Failed to start `dotnet run` for {ProjectRelativePath}");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, _stdout, true));
        _ = Task.Run(() => DrainAsync(_process.StandardError, _stderr, false));

        using var cts = new CancellationTokenSource(ReadyTimeout);
        var url = await _baseUrl.Task.WaitAsync(cts.Token);
        await WaitForReadyAsync(url, cts.Token);
        await VerifyScopedAssetsServedAsync(repoRoot, url, cts.Token);
    }

    /// <summary>
    ///     Fail fast (and descriptively) if the served bundle is missing its baked
    ///     per-component scoped assets. WasmAppHost serves the project's static web assets
    ///     (<c>--use-staticwebassets</c>): the <c>/_rask/a/{hash}.{ext}</c> files are baked by
    ///     <c>Rask.Wasm.Tasks.BakeScopedAssetsTask</c> into the intermediate staging dir and
    ///     registered as computed static web assets (see <c>_RaskBakeScopedStaticWebAssets</c>
    ///     in <c>Rask.Wasm/build/Rask.Wasm.targets</c>). If absent, every CodeSample page 404s
    ///     on <c>window.Rask.CodeSample</c> and highlighting never runs — which would otherwise
    ///     surface as five separate ~5s Playwright "locator never visible" timeouts with no
    ///     hint at the cause; with this probe the whole collection fails once, here, with the
    ///     missing URL and the staging directory named.
    /// </summary>
    private static async Task VerifyScopedAssetsServedAsync(string repoRoot, string url, CancellationToken ct)
    {
        var scopedDir = Path.Combine(repoRoot, ProjectRelativePath, "obj",
            Configuration, "net10.0-browser", "rask-scoped", "_rask", "a");

        var jsFile = Directory.Exists(scopedDir)
            ? Directory.EnumerateFiles(scopedDir, "*.js").FirstOrDefault()
            : null;
        if (jsFile is null)
        {
            throw new InvalidOperationException(
                $"{ProjectRelativePath} is missing baked scoped-JS assets under '{scopedDir}'. " +
                "The BakeScopedAssetsTask bake did not produce /_rask/a/*.js — standalone WASM would 404 on " +
                "every scoped-asset URL and highlight/JS-interop tests would all time out. " +
                "See Rask.Wasm/build/Rask.Wasm.targets (_RaskBakeScopedStaticWebAssets).");
        }

        var assetUrl = $"{url}/_rask/a/{Path.GetFileName(jsFile)}";
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        using var resp = await http.GetAsync(assetUrl, ct);
        if (resp.StatusCode != HttpStatusCode.OK)
        {
            throw new InvalidOperationException(
                $"{ProjectRelativePath} served {assetUrl} with HTTP {(int)resp.StatusCode} (expected 200). " +
                $"The baked file exists on disk at '{jsFile}' but the static host did not serve it.\n{url}");
        }
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

    private async Task WaitForReadyAsync(string url, CancellationToken ct)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        while (!ct.IsCancellationRequested)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"{ProjectRelativePath} exited before becoming ready (code {_process.ExitCode}).\n{ServerLog}");
            }

            try
            {
                using var resp = await http.GetAsync($"{url}/index.html", ct);
                if ((int)resp.StatusCode < 500)
                {
                    return;
                }
            }
            catch (HttpRequestException)
            {
                /* not yet listening */
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested)
            {
                /* per-request timeout */
            }

            await Task.Delay(250, ct);
        }

        throw new TimeoutException(
            $"{ProjectRelativePath} did not respond on {url} within {ReadyTimeout}.\n{ServerLog}");
    }

    private async Task DrainAsync(StreamReader reader, StringBuilder buffer, bool captureUrl)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_logLock)
            {
                buffer.AppendLine(line);
            }

            if (captureUrl && !_baseUrl.Task.IsCompleted)
            {
                var m = AppUrlPattern.Match(line);
                if (m.Success)
                {
                    _baseUrl.TrySetResult(m.Groups[1].Value);
                }
            }
        }
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
