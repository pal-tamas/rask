using System.Diagnostics;
using Rask.Cli.Scaffolding;

namespace Rask.Templates.E2E.Tests;

/// <summary>
///     A scaffolded meta app is built into its container and booted, and the forwarder is exercised.
/// </summary>
/// <remarks>
///     <para>
///         This is #946's Risk 1, and the one gap nothing else in the repository covers. In development
///         the browser talks to the framework's own dev server directly (decision 9 of #946), so
///         Kestrel's forwarder — its WebSocket and streaming paths, its supervision of Node as a child,
///         the static roots it serves itself — is exercised at DEPLOY time and nowhere else. Until this
///         existed, the first thing that ran the arrangement for real was production.
///     </para>
///     <para>
///         A container rather than <c>dotnet run</c> on purpose: the image is where the two toolchains
///         meet. It carries a Node runtime beside the .NET one, runs `npm ci` and a production framework
///         build inside itself, and ends up running `node &lt;entry&gt;` as a child of the host — none of
///         which a local run proves, because a local run has the developer's own Node on PATH.
///     </para>
///     <para>
///         Its own switch: this is a docker build of a framework app from cold, which is the most
///         expensive thing in the repository. One framework by default — the point is the forwarder,
///         which is shared — and RASK_META_CONTAINER_ALL=1 for every one of the six.
///     </para>
/// </remarks>
public sealed class MetaContainerBootE2ETests
{
    private const string SkipReason =
        "Meta container gate: set RASK_META_CONTAINER_E2E=1 (with RASK_TEMPLATE_E2E=1) to run it. It "
        + "builds a Docker image that runs npm ci and a production framework build inside itself, then "
        + "boots it. Needs Docker. See scripts/run-template-e2e.sh --container.";

    private static bool Enabled =>
        TemplateBuildE2ETests.Enabled
        && Environment.GetEnvironmentVariable("RASK_META_CONTAINER_E2E") == "1";

    /// <summary>
    ///     Nuxt alone unless asked for all six. What is under test is the forwarder and the supervisor,
    ///     which every meta template shares; the per-framework differences (which entry Node runs, where
    ///     the static root is) are covered by MetaFramework's own tests and by the build gate.
    /// </summary>
    public static TheoryData<string> Frameworks() =>
        Environment.GetEnvironmentVariable("RASK_META_CONTAINER_ALL") == "1"
            ? [.. MetaTemplate.All.Select(f => f.Key)]
            : ["nuxt"];

    [SkippableTheory]
    [MemberData(nameof(Frameworks))]
    public async Task The_container_boots_and_Kestrel_forwards_to_node(string key)
    {
        Skip.IfNot(Enabled, SkipReason);
        Skip.IfNot(await DockerIsUsableAsync(), "Docker is not running.");

        var (feed, version) = await CliBuildE2E.LocalFeed.Value;
        var name = "Ctr" + key.Replace("-", "", StringComparison.Ordinal);
        var work = TemplateBuildE2ETests.NewWorkingDirectory();
        var projectDirectory = Path.Combine(work, name);
        var image = $"rask-template-gate/{key}:{Guid.NewGuid():N}";
        var container = $"rask-gate-{key}-{Guid.NewGuid():N}"[..40];

        var result = TemplateBuildE2ETests.Scaffold(key, projectDirectory, name, version, islands: []);
        TemplateBuildE2ETests.Write(result, projectDirectory, feed);

        // The image restores Rask from NuGet, so the packages this commit produced have to be reachable
        // from inside the build. The scaffold's nuget.config names a host path; copying the feed in and
        // pointing at it is the smallest change that keeps the Dockerfile itself untouched — and the
        // Dockerfile is part of what is under test.
        var feedInside = Path.Combine(projectDirectory, "local-feed");
        CopyDirectory(feed, feedInside);
        RewriteNuGetConfig(projectDirectory);

        try
        {
            var (built, buildLog) = await Docker(
                $"build -t {image} -f Dockerfile .", projectDirectory, TimeSpan.FromMinutes(30));

            Assert.True(built == 0, $"--template {key}: docker build failed.\n{Tail(buildLog)}");

            var port = LoopbackPort.Reserve();
            var (started, runLog) = await Docker(
                $"run -d --name {container} -p {port}:8080 {image}", projectDirectory, TimeSpan.FromMinutes(2));

            Assert.True(started == 0, $"--template {key}: docker run failed.\n{Tail(runLog)}");

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            var baseUrl = $"http://localhost:{port}";
            var html = await WaitForPageAsync(http, baseUrl, container);

            // Node rendered this and Kestrel forwarded it, inside one container, on one port.
            Assert.Contains("<h1", html, StringComparison.OrdinalIgnoreCase);

            // And the host still owns its own routes rather than forwarding them — the failure that
            // reads as a front-end bug because an API call comes back as a page.
            var api = await http.GetAsync($"{baseUrl}/_rask/does-not-exist");
            var body = await api.Content.ReadAsStringAsync();

            Assert.False(
                body.Contains("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<html", StringComparison.OrdinalIgnoreCase),
                $"--template {key}: the container answered a /_rask request with a rendered page, so "
                + $"Kestrel forwarded what it should have handled.\n{Tail(body)}\n\n{await LogsAsync(container)}");
        }
        finally
        {
            await Docker($"rm -f {container}", projectDirectory, TimeSpan.FromMinutes(2));
            await Docker($"image rm -f {image}", projectDirectory, TimeSpan.FromMinutes(2));
            CliBuildE2E.TryDeleteDirectory(work);
        }
    }

    /// <summary>
    ///     Waits out the documented startup window and refuses to wait out anything else.
    /// </summary>
    /// <remarks>
    ///     A meta host answers <b>503</b> while it waits for Node to bind, and that is by design. Any
    ///     other failing status is a real answer and is reported immediately rather than retried until
    ///     the timeout — a gate that waits out a 500 turns a clear failure into "it never came up".
    /// </remarks>
    private static async Task<string> WaitForPageAsync(HttpClient http, string baseUrl, string container)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromMinutes(3);

        while (DateTime.UtcNow < deadline)
        {
            try
            {
                var response = await http.GetAsync(baseUrl);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                if (response.StatusCode != System.Net.HttpStatusCode.ServiceUnavailable)
                {
                    Assert.Fail(
                        $"the container answered {(int)response.StatusCode} at / — not the 503 it "
                        + $"reports while Node starts.\n{await LogsAsync(container)}");
                }
            }
            catch (HttpRequestException)
            {
                // Not accepting connections yet.
            }

            await Task.Delay(1000);
        }

        Assert.Fail($"the container never answered {baseUrl}.\n{await LogsAsync(container)}");
        return string.Empty;
    }

    private static async Task<string> LogsAsync(string container)
    {
        var (_, log) = await Docker($"logs --tail 60 {container}", Path.GetTempPath(), TimeSpan.FromMinutes(1));
        return $"container log:\n{log}";
    }

    private static async Task<bool> DockerIsUsableAsync()
    {
        var (exit, _) = await Docker("info --format {{.ServerVersion}}", Path.GetTempPath(), TimeSpan.FromSeconds(30));
        return exit == 0;
    }

    private static async Task<(int Exit, string Output)> Docker(
        string arguments, string workingDirectory, TimeSpan timeout)
    {
        var startInfo = new ProcessStartInfo("docker")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        try
        {
            await process.WaitForExitAsync().WaitAsync(timeout);
        }
        catch (TimeoutException)
        {
            process.Kill(entireProcessTree: true);
            return (-1, $"`docker {arguments}` did not finish within {timeout}.");
        }

        return (process.ExitCode, await stdout + await stderr);
    }

    /// <summary>
    ///     Points the scaffold's nuget.config at the feed copied INTO the build context.
    /// </summary>
    /// <remarks>
    ///     The scaffolded config names an absolute host path, which does not exist inside the image.
    ///     Rewriting it rather than editing the Dockerfile keeps the Dockerfile exactly as a user gets
    ///     it — it is one of the things under test.
    /// </remarks>
    private static void RewriteNuGetConfig(string projectDirectory) =>
        File.WriteAllText(
            Path.Combine(projectDirectory, "nuget.config"),
            """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear/>
                <add key="local" value="local-feed"/>
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json"/>
              </packageSources>
            </configuration>
            """);

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)), overwrite: true);
        }
    }

    private static string Tail(string output)
    {
        var lines = output.Split('\n');
        return string.Join('\n', lines[Math.Max(0, lines.Length - 40)..]);
    }
}
