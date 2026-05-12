using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
/// Boots <c>Rask.Example.Wasm</c> standalone via WasmAppHost (the dev launcher that
/// `dotnet run` invokes for browser-wasm projects). WasmAppHost picks a random port
/// and prints <c>App url: http://127.0.0.1:{port}/index.html</c> — we parse that
/// from stdout instead of forcing a fixed --urls (WasmAppHost ignores ASP.NET
/// hosting args).
///
/// Note: WasmAppHost does NOT install a SPA fallback. Deep-link reloads at
/// /events etc. return 404. Tests that exercise this fixture must always start at
/// the root and navigate via sidebar buttons.
/// </summary>
public sealed class StandaloneWasmAppFixture : IAsyncLifetime
{
    private const string ProjectRelativePath = "Rask.Example.Wasm";
    private Process? _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly Lock _logLock = new();
    private readonly TaskCompletionSource<string> _baseUrl = new();
    private static readonly Regex AppUrlPattern =
        new(@"App url:\s*(http://[^\s/]+)/index\.html", RegexOptions.Compiled);

    private TimeSpan ReadyTimeout { get; } = TimeSpan.FromSeconds(180);

    public string BaseUrl => _baseUrl.Task.IsCompletedSuccessfully
        ? _baseUrl.Task.Result
        : throw new InvalidOperationException(
            "BaseUrl read before WasmAppHost emitted 'App url:' line. Did InitializeAsync complete?");

    public string ServerLog
    {
        get { lock (_logLock) return $"{_stdout}\n--- STDERR ---\n{_stderr}"; }
    }

    public async Task InitializeAsync()
    {
        var repoRoot = LocateRepoRoot();
        var projectPath = Path.Combine(repoRoot, ProjectRelativePath);

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "run", "--project", projectPath,
                "--no-launch-profile",
                "-c", "Debug"
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start `dotnet run` for {ProjectRelativePath}");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, _stdout, captureUrl: true));
        _ = Task.Run(() => DrainAsync(_process.StandardError, _stderr, captureUrl: false));

        using var cts = new CancellationTokenSource(ReadyTimeout);
        var url = await _baseUrl.Task.WaitAsync(cts.Token);
        await WaitForReadyAsync(url, cts.Token);
    }

    public async Task DisposeAsync()
    {
        if (_process is null) return;
        if (!_process.HasExited)
        {
            try { _process.Kill(entireProcessTree: true); }
            catch { /* race: already exited */ }
        }
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            await _process.WaitForExitAsync(cts.Token);
        }
        catch { /* timed out — best effort */ }
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
                if ((int)resp.StatusCode < 500) return;
            }
            catch (HttpRequestException) { /* not yet listening */ }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { /* per-request timeout */ }

            await Task.Delay(250, ct);
        }

        throw new TimeoutException(
            $"{ProjectRelativePath} did not respond on {url} within {ReadyTimeout}.\n{ServerLog}");
    }

    private async Task DrainAsync(StreamReader reader, StringBuilder buffer, bool captureUrl)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_logLock) buffer.AppendLine(line);
            if (captureUrl && !_baseUrl.Task.IsCompleted)
            {
                var m = AppUrlPattern.Match(line);
                if (m.Success) _baseUrl.TrySetResult(m.Groups[1].Value);
            }
        }
    }

    private static string LocateRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "Rask.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException(
            $"Could not locate Rask.sln walking up from {AppContext.BaseDirectory}");
    }
}
