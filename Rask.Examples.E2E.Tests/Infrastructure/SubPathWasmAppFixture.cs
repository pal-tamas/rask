using System.Diagnostics;
using System.Text;

namespace Rask.Examples.E2E.Tests.Infrastructure;

/// <summary>
///     Boots <c>Rask.Example.Wasm.Host</c> under a sub-path (<c>/sub</c>) backed by a
///     <c>Rask.Example.Wasm</c> AppBundle published with <c>/p:RaskPathBase=/sub</c>
///     so the bundled <c>index.html</c> already has <c>&lt;base href="/sub/"&gt;</c>.
///     The host process reads <c>RASK_BUNDLE_DIR</c> + <c>RASK_PATHBASE</c> at startup
///     (see <c>Rask.Example.Wasm.Host/Program.cs</c>) and threads them into
///     <c>UseRask&lt;App&gt;(bundlePath, pathBase)</c>. Exposes <see cref="BaseUrl"/>
///     pointing at the prefix root (<c>http://localhost:{port}/sub</c>) so tests can
///     navigate via Playwright without rewriting every path.
/// </summary>
public sealed class SubPathWasmAppFixture : IAsyncLifetime
{
#if DEBUG
    private const string Configuration = "Debug";
#else
    private const string Configuration = "Release";
#endif

    // 5099 is ServerExampleAppFixture, 5098 is WasmExampleAppFixture; these collections
    // run in parallel, so this fixture needs its own port to avoid an "address already in
    // use" bind clash that crashes both hosts.
    private const int Port = 5097;
    private const string PathBase = "/sub";

    private readonly Lock _logLock = new();
    private readonly StringBuilder _stderr = new();
    private readonly StringBuilder _stdout = new();
    private Process? _process;
    private string? _bundleDir;

    public string BaseUrl => $"http://localhost:{Port}{PathBase}";
    public string OriginUrl => $"http://localhost:{Port}";

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

        // Copy the already-built Rask.Example.Wasm AppBundle to a temp dir and
        // rewrite its <base href> locally. Publishing with /p:RaskPathBase=/sub
        // would re-trigger the framework's _RaskRewriteBaseHref MSBuild target
        // against the shared bin/.../AppBundle/index.html — corrupting the same
        // file the parallel WasmExample fixture reads (it serves
        // bin/.../AppBundle/index.html in-place via Rask.Example.Wasm.Host).
        // Copy-and-sed leaves bin untouched.
        var srcBundle = Path.Combine(repoRoot, "Rask.Example.Wasm", "bin",
            Configuration, "net10.0-browser", "browser-wasm", "AppBundle");
        if (!Directory.Exists(srcBundle))
        {
            throw new InvalidOperationException(
                $"Pre-built Rask.Example.Wasm AppBundle not found at {srcBundle}. " +
                "The fixture relies on the main test-suite build having produced it.");
        }

        _bundleDir = Path.Combine(Path.GetTempPath(),
            "rask-subpath-e2e-" + Guid.NewGuid().ToString("N"));
        var appBundle = Path.Combine(_bundleDir, "AppBundle");
        CopyDirectory(srcBundle, appBundle);

        var indexHtmlPath = Path.Combine(appBundle, "index.html");
        var indexHtml = await File.ReadAllTextAsync(indexHtmlPath);
        var rewritten = indexHtml.Replace("<base href=\"/\"/>", $"<base href=\"{PathBase}/\"/>");
        if (ReferenceEquals(indexHtml, rewritten) || indexHtml == rewritten)
        {
            throw new InvalidOperationException(
                "Failed to rewrite <base href=\"/\"> in copied index.html — bundle layout drift?");
        }
        await File.WriteAllTextAsync(indexHtmlPath, rewritten);

        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList =
            {
                "run",
                "--project", Path.Combine(repoRoot, "Rask.Example.Wasm.Host"),
                "--no-launch-profile",
                "--no-build",
                "-c", Configuration,
                "--", "--urls", OriginUrl
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.Environment["ASPNETCORE_URLS"] = OriginUrl;
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";
        psi.Environment["RASK_BUNDLE_DIR"] = appBundle;
        psi.Environment["RASK_PATHBASE"] = PathBase;

        _process = Process.Start(psi)
                   ?? throw new InvalidOperationException("Failed to start Rask.Example.Wasm.Host with sub-path env vars");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, _stdout));
        _ = Task.Run(() => DrainAsync(_process.StandardError, _stderr));

        await WaitForReadyAsync(TimeSpan.FromSeconds(120));
    }

    public async Task DisposeAsync()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                try { _process.Kill(true); }
                catch { /* race: already exited */ }
            }
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                await _process.WaitForExitAsync(cts.Token);
            }
            catch { /* best effort */ }
            _process.Dispose();
        }

        if (_bundleDir is not null)
        {
            try { Directory.Delete(_bundleDir, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(dir.Replace(source, destination));
        }
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, file.Replace(source, destination), overwrite: true);
        }
    }

    private async Task WaitForReadyAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (_process!.HasExited)
            {
                throw new InvalidOperationException(
                    $"Sub-path host exited before becoming ready (code {_process.ExitCode}).\n{ServerLog}");
            }
            try
            {
                using var resp = await http.GetAsync($"{BaseUrl}/");
                if ((int)resp.StatusCode < 500) return;
            }
            catch (HttpRequestException) { /* not yet listening */ }
            catch (TaskCanceledException) { /* per-request timeout */ }
            await Task.Delay(250);
        }
        throw new TimeoutException(
            $"Sub-path host did not respond on {BaseUrl} within {timeout}.\n{ServerLog}");
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
