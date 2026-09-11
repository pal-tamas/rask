using System.Diagnostics;
using System.Text;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     Runs a scaffolded app's host out of process and waits for it to answer.
/// </summary>
/// <remarks>
///     <para>
///         Purpose-built rather than reusing <c>Rask.Site.E2E.Tests</c>'s fixture, which resolves its
///         project relative to the repository root — these projects live in a scratch directory and
///         exist only for the length of one test.
///     </para>
///     <para>
///         It runs the host the way a user does, with <c>dotnet run --no-build</c> against the build the
///         test has already made. On the meta lane that one process is two: the host supervises the
///         framework's Node server as a child and forwards to it over loopback, which is exactly the
///         arrangement the journeys are here to exercise.
///     </para>
/// </remarks>
internal sealed class ScaffoldedHost : IAsyncDisposable
{
    private readonly StringBuilder _log = new();
    private readonly Lock _logLock = new();
    private readonly Process _process;

    private ScaffoldedHost(Process process, int port)
    {
        _process = process;
        Port = port;
    }

    public int Port { get; }

    public string BaseUrl => $"http://localhost:{Port}";

    /// <summary>Everything the host wrote, for an assertion message that says why it failed.</summary>
    public string Log
    {
        get
        {
            lock (_logLock)
            {
                return _log.ToString();
            }
        }
    }

    /// <summary>
    ///     Starts the host in <paramref name="projectDirectory"/> and returns once it answers.
    /// </summary>
    /// <param name="projectDirectory">The scaffolded project's directory.</param>
    /// <param name="projectFile">The .csproj to run.</param>
    /// <param name="readyTimeout">
    ///     How long to wait. Generous on the meta lane: the host starts Node and waits for the
    ///     framework's own server to bind before it answers anything.
    /// </param>
    public static async Task<ScaffoldedHost> StartAsync(
        string projectDirectory, string projectFile, TimeSpan readyTimeout)
    {
        var port = LoopbackPort.Reserve();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = projectDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in new[]
        {
            "run", "--no-build", "--project", projectFile,

            // WITHOUT this the scaffolded Properties/launchSettings.json wins and ASPNETCORE_URLS
            // below is ignored, so every host binds the profile's port instead of its reserved one.
            // On this machine that is port 5000, which macOS Control Center already holds — so the
            // host aborts with "address already in use" and the journey reads as "it never answered".
            "--no-launch-profile",

            // The front end is already built; a `dotnet run` that rebuilt it would add minutes and
            // could pick a different bundle than the one this test just proved.
            "-p:RaskSpaBuild=false", "-p:RaskMetaBuild=false", "-p:RaskExternalBuild=false",
        })
        {
            startInfo.ArgumentList.Add(argument);
        }

        startInfo.Environment["ASPNETCORE_URLS"] = $"http://localhost:{port}";
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";
        // Node reuse keeps a worker alive that has already loaded a task assembly from an earlier
        // run's scratch directory; the next run's load of the same name from a new path throws.
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";

        var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"could not start the host in {projectDirectory}");

        var host = new ScaffoldedHost(process, port);
        process.OutputDataReceived += (_, e) => host.Append(e.Data);
        process.ErrorDataReceived += (_, e) => host.Append(e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await host.WaitUntilAnsweringAsync(readyTimeout);
        }
        catch
        {
            // The caller never receives the host when this throws, so nothing else will ever dispose
            // it — and a host that failed its readiness check is often still running and still holding
            // a port. One leak like that took out two unrelated tests in a later suite, which read as a
            // regression in THEM rather than as a leak from here.
            await host.DisposeAsync();
            throw;
        }

        return host;
    }

    private void Append(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_logLock)
        {
            _log.AppendLine(line);
        }
    }

    private async Task WaitUntilAnsweringAsync(TimeSpan timeout)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        var deadline = DateTime.UtcNow + timeout;

        while (DateTime.UtcNow < deadline)
        {
            if (_process.HasExited)
            {
                throw new InvalidOperationException(
                    $"the host exited with code {_process.ExitCode} before answering.\n{Log}");
            }

            try
            {
                using var response = await http.GetAsync(BaseUrl);

                // A meta host answers 503 by design while it waits for Node to bind — that is a
                // documented startup window, not a failure, so it is waited out rather than accepted.
                if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
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
                // Listening but slow — a first request can pay a cold JIT.
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException($"the host did not answer {BaseUrl} within {timeout}.\n{Log}");
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            // The whole tree: a meta host has a Node child, and killing only the parent leaves a
            // server holding the port for the next test.
            _process.Kill(entireProcessTree: true);
            try
            {
                await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            }
            catch (TimeoutException)
            {
                // Nothing useful left to do; the scratch directory goes with the test either way.
            }
        }

        _process.Dispose();
    }
}
