using System.Diagnostics;
using System.Text;

namespace Rask.Examples.E2E.Tests.Infrastructure;

public abstract class ExampleAppFixture : IAsyncLifetime
{
    private Process? _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly Lock _logLock = new();

    protected abstract string ProjectRelativePath { get; }
    protected abstract int Port { get; }
    protected virtual TimeSpan ReadyTimeout => TimeSpan.FromSeconds(120);

    public string BaseUrl => $"http://localhost:{Port}";

    public string ServerLog
    {
        get
        {
            lock (_logLock) return $"{_stdout}\n--- STDERR ---\n{_stderr}";
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
                "run", "--project", projectPath,
                "--no-launch-profile",
                "-c", "Debug",
                "--", "--urls", BaseUrl
            },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = repoRoot
        };
        psi.Environment["ASPNETCORE_URLS"] = BaseUrl;
        psi.Environment["DOTNET_ENVIRONMENT"] = "Production";

        _process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start `dotnet run` for {ProjectRelativePath}");

        _ = Task.Run(() => DrainAsync(_process.StandardOutput, _stdout));
        _ = Task.Run(() => DrainAsync(_process.StandardError, _stderr));

        await WaitForReadyAsync();
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
                if ((int)resp.StatusCode < 500) return;
            }
            catch (HttpRequestException) { /* not yet listening */ }
            catch (TaskCanceledException) { /* per-request timeout */ }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"{ProjectRelativePath} did not respond on {BaseUrl} within {ReadyTimeout}.\n{ServerLog}");
    }

    private async Task DrainAsync(StreamReader reader, StringBuilder buffer)
    {
        while (await reader.ReadLineAsync() is { } line)
        {
            lock (_logLock) buffer.AppendLine(line);
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
