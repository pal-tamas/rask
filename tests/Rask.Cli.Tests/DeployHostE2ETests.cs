using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The deploy gate: run the real <c>rask deploy</c> against a container standing in for a bare VPS and
/// assert on what actually happened <b>on the host</b> — an image that built, a container that answers
/// HTTP, a Caddyfile a real Caddy accepted, a volume whose contents outlived the container.
///
/// <para>These are the assertions the mocked suite structurally cannot make. <see cref="DeployCommandTests"/>
/// proves <c>docker run</c> is spelled correctly; this proves the thing it spells actually serves.</para>
/// </summary>
public sealed class DeployHostE2ETests(DeployHostFixture host) : IClassFixture<DeployHostFixture>
{
    /// <summary>
    /// The app under deployment. Deliberately tiny (busybox httpd, no .NET) so the gate measures the deploy
    /// path rather than a SDK image pull: the mechanics being tested — build on the box, health-gate, swap,
    /// mount the volume — are identical whatever the image contains. Each boot appends to a file under
    /// <c>/data</c>, which is how volume persistence is observed across container replacement.
    /// </summary>
    private const string AppDockerfile = """
        FROM docker.io/library/nginx:alpine
        RUN mkdir -p /data \
         && printf 'server { listen 8080; location /health { return 200 "ok"; } }\n' > /etc/nginx/conf.d/default.conf
        EXPOSE 8080
        CMD ["/bin/sh","-c","echo boot >> /data/boots.txt; exec nginx -g 'daemon off;'"]
        """;

    [SkippableFact]
    public async Task Deploy_to_a_port_builds_runs_and_answers_its_health_check()
    {
        var project = Start(out var console, out var command);

        var exit = await command.ExecuteAsync(["--host", host.Host, "--port", "8080", "--name", "portapp"], CancellationToken.None);

        Assert.True(exit == 0, $"deploy failed.\n{console.OutText}\n{console.ErrorText}");
        Assert.Contains("Deployed.", console.OutText, StringComparison.Ordinal);

        // The container is genuinely up on the host, and the app answers on the published port.
        var (_, running) = await host.DockerAsync("ps", "--filter", "name=portapp", "--format", "{{.Names}} {{.Status}}");
        Assert.Contains("portapp", running, StringComparison.Ordinal);
        Assert.Contains("Up", running, StringComparison.Ordinal);

        var (probeExit, body) = await host.OnHostAsync("wget -qO- http://127.0.0.1:8080/health");
        Assert.True(probeExit == 0, $"the deployed app didn't answer on the published port: {body}");
        Assert.Contains("ok", body, StringComparison.Ordinal);

        // The host is the source of truth for the next deploy, so the config it remembers must be real.
        Assert.True(File.Exists(Path.Combine(project, ".rask", "deploy.json")), "deploy.json wasn't persisted.");
    }

    /// <summary>
    /// The database-survives-redeploy contract. Every deploy runs a <em>fresh container</em>, so the SQLite
    /// file only survives because <c>rask deploy</c> mounts a per-app named volume at <c>/data</c> — the
    /// single most destructive thing to get wrong, and previously asserted only against a mock.
    /// </summary>
    [SkippableFact]
    public async Task Redeploy_keeps_the_data_volume()
    {
        var project = Start(out var console, out var command);
        string[] args = ["--host", host.Host, "--port", "8081", "--name", "dataapp"];

        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"first deploy failed.\n{console.ErrorText}");
        var (_, first) = await host.DockerAsync("exec", "dataapp", "cat", "/data/boots.txt");
        Assert.Equal(1, CountBoots(first));

        // A second deploy replaces the container entirely — same volume, so the file must accumulate.
        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"redeploy failed.\n{console.ErrorText}");
        var (_, second) = await host.DockerAsync("exec", "dataapp", "cat", "/data/boots.txt");
        Assert.Equal(2, CountBoots(second));

        Assert.True(File.Exists(Path.Combine(project, ".rask", "deploy.json")));
    }

    /// <summary>
    /// The zero-downtime path: a blue-green swap behind the shared Caddy proxy. This is the one that needs a
    /// real host most — the Caddyfile is generated as text and only a running Caddy can say whether it is
    /// valid, and the colour bookkeeping is read back from live container labels.
    /// </summary>
    [SkippableFact]
    public async Task Domain_deploy_swaps_colour_behind_a_real_caddy()
    {
        Start(out var console, out var command);
        string[] args = ["--host", host.Host, "--domain", "rask-e2e.test", "--name", "webapp"];

        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"first domain deploy failed.\n{console.OutText}\n{console.ErrorText}");

        var (_, afterFirst) = await host.DockerAsync("ps", "--format", "{{.Names}}");
        Assert.Contains("webapp-blue", afterFirst, StringComparison.Ordinal);
        Assert.Contains("rask-caddy", afterFirst, StringComparison.Ordinal);

        // A real Caddy accepted the generated Caddyfile — `caddy reload` validates before applying, so the
        // deploy returning 0 already means it parsed. Assert the route landed in the running config too.
        var (_, caddyfile) = await host.DockerAsync("exec", "rask-caddy", "cat", "/etc/caddy/Caddyfile");
        Assert.Contains("rask-e2e.test", caddyfile, StringComparison.Ordinal);
        Assert.Contains("webapp-blue:8080", caddyfile, StringComparison.Ordinal);

        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"second domain deploy failed.\n{console.ErrorText}");

        // Green took over and blue was retired — the swap, observed on the host rather than in argv.
        var (_, afterSecond) = await host.DockerAsync("ps", "--format", "{{.Names}}");
        Assert.Contains("webapp-green", afterSecond, StringComparison.Ordinal);
        Assert.DoesNotContain("webapp-blue", afterSecond, StringComparison.Ordinal);

        var (_, reloaded) = await host.DockerAsync("exec", "rask-caddy", "cat", "/etc/caddy/Caddyfile");
        Assert.Contains("webapp-green:8080", reloaded, StringComparison.Ordinal);
    }

    /// <summary>
    /// A container that starts but never answers must not take the domain. The old version keeps serving and
    /// the failed colour is removed — the rollback that only exists at deploy time, proven end to end.
    /// </summary>
    [SkippableFact]
    public async Task A_failing_health_check_leaves_the_previous_version_serving()
    {
        var project = Start(out var console, out var command);
        string[] good = ["--host", host.Host, "--domain", "rask-health.test", "--name", "healthapp"];
        Assert.True(await command.ExecuteAsync(good, CancellationToken.None) == 0, $"baseline deploy failed.\n{console.ErrorText}");

        // Replace the app with one that runs but serves nothing, then redeploy: the probe must fail.
        File.WriteAllText(
            Path.Combine(project, "Dockerfile"),
            """
            FROM docker.io/library/alpine:3.20
            EXPOSE 8080
            CMD ["sleep", "600"]
            """);

        var exit = await command.ExecuteAsync(good, CancellationToken.None);

        Assert.True(exit != 0, "a deploy whose health probe never passes must fail.");
        var (_, containers) = await host.DockerAsync("ps", "--format", "{{.Names}}");
        Assert.Contains("healthapp-blue", containers, StringComparison.Ordinal);   // the good one still serves
        Assert.DoesNotContain("healthapp-green", containers, StringComparison.Ordinal); // the bad one was removed

        var (_, caddyfile) = await host.DockerAsync("exec", "rask-caddy", "cat", "/etc/caddy/Caddyfile");
        Assert.Contains("healthapp-blue:8080", caddyfile, StringComparison.Ordinal);
    }

    /// <summary>
    /// An app that listens somewhere other than 8080 — the shape the standalone <c>wasm</c> template had
    /// (an nginx image on port 80) and which was undeployable, because the readiness probe and the proxy
    /// both aimed at a hardcoded 8080. Proven here against a real host: the probe has to actually connect.
    /// </summary>
    [SkippableFact]
    public async Task An_app_on_a_non_default_container_port_deploys()
    {
        var project = Start(out var console, out var command);
        File.WriteAllText(
            Path.Combine(project, "Dockerfile"),
            """
            FROM docker.io/library/nginx:alpine
            RUN printf 'server { listen 3000; location /health { return 200 "ok"; } }\n' > /etc/nginx/conf.d/default.conf
            EXPOSE 3000
            CMD ["nginx", "-g", "daemon off;"]
            """.ReplaceLineEndings("\n"));

        var exit = await command.ExecuteAsync(
            ["--host", host.Host, "--domain", "rask-port.test", "--name", "portapp3000", "--container-port", "3000"],
            CancellationToken.None);

        Assert.True(exit == 0, $"deploy on a non-default container port failed.\n{console.OutText}\n{console.ErrorText}");

        // The proxy points at 3000, and the port was remembered as a label on the container itself so a
        // later deploy of a *different* app can regenerate the shared Caddyfile without losing it.
        var (_, caddyfile) = await host.DockerAsync("exec", "rask-caddy", "cat", "/etc/caddy/Caddyfile");
        Assert.Contains("portapp3000-blue:3000", caddyfile, StringComparison.Ordinal);

        var (_, label) = await host.DockerAsync("inspect", "--format", "{{index .Config.Labels \"rask.port\"}}", "portapp3000-blue");
        Assert.Equal("3000", label.Trim());
    }

    /// <summary>
    /// The day-two verbs, against a real deployment. Rollback is the one that most needs a live host: it
    /// depends on image tags surviving a build that reuses them, which no mock can tell you.
    /// </summary>
    [SkippableFact]
    public async Task Status_logs_and_rollback_operate_on_the_live_deployment()
    {
        var project = Start(out var console, out var command);
        string[] args = ["--host", host.Host, "--domain", "rask-ops.test", "--name", "opsapp"];

        // v1 — the version we will roll back to. A marker in the served body identifies it.
        File.WriteAllText(Path.Combine(project, "Dockerfile"), AppWithMarker("VERSION-ONE"));
        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"v1 deploy failed.\n{console.ErrorText}");

        // v2 — starts and answers, so every deploy-time gate passes. It's simply the wrong build; that is
        // precisely the failure the blue-green rollback cannot help with, and what `rollback` is for.
        File.WriteAllText(Path.Combine(project, "Dockerfile"), AppWithMarker("VERSION-TWO"));
        Assert.True(await command.ExecuteAsync(args, CancellationToken.None) == 0, $"v2 deploy failed.\n{console.ErrorText}");
        Assert.Equal("VERSION-TWO", await ServedBodyAsync("opsapp"));

        var status = new StringConsole();
        var statusCommand = Command(project, status);
        Assert.Equal(0, await statusCommand.ExecuteAsync(["status", "--host", host.Host, "--name", "opsapp"], CancellationToken.None));
        Assert.Contains("opsapp", status.OutText, StringComparison.Ordinal);
        Assert.Contains("rask-ops.test", status.OutText, StringComparison.Ordinal);
        Assert.Contains("rollback", status.OutText, StringComparison.Ordinal); // ...and that one is possible

        var logs = new StringConsole();
        Assert.Equal(0, await Command(project, logs).ExecuteAsync(["logs", "--host", host.Host, "--name", "opsapp", "--tail", "10"], CancellationToken.None));

        var back = new StringConsole();
        var exit = await Command(project, back).ExecuteAsync(["rollback", "--host", host.Host, "--name", "opsapp"], CancellationToken.None);
        Assert.True(exit == 0, $"rollback failed.\n{back.OutText}\n{back.ErrorText}");

        // The proof: the live site serves v1 again, through the proxy, after a health-gated swap.
        Assert.Equal("VERSION-ONE", await ServedBodyAsync("opsapp"));

        // ...and rolling back again returns to v2, because the tags were swapped rather than consumed.
        var forward = new StringConsole();
        Assert.Equal(0, await Command(project, forward).ExecuteAsync(["rollback", "--host", host.Host, "--name", "opsapp"], CancellationToken.None));
        Assert.Equal("VERSION-TWO", await ServedBodyAsync("opsapp"));
    }

    [SkippableFact]
    public async Task Rollback_refuses_when_there_is_no_previous_image()
    {
        var project = Start(out var console, out var command);
        Assert.True(
            await command.ExecuteAsync(["--host", host.Host, "--port", "8090", "--name", "onlyonce"], CancellationToken.None) == 0,
            $"first deploy failed.\n{console.ErrorText}");

        var back = new StringConsole();
        var exit = await Command(project, back).ExecuteAsync(["rollback", "--host", host.Host, "--name", "onlyonce"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("nothing to roll back to", back.ErrorText, StringComparison.Ordinal);
    }

    /// <summary>
    /// Secrets now reach the container through a file rather than the command line, and a redeploy that
    /// forgets one is refused. Both are worth proving against a real daemon: the first only works if
    /// docker actually applies <c>--env-file</c>, and the second has to survive a real round-trip through
    /// <c>.rask/deploy.json</c>.
    /// </summary>
    [SkippableFact]
    public async Task Runtime_env_reaches_the_container_and_is_required_on_the_next_deploy()
    {
        var project = Start(out var console, out var command);
        string[] args = ["--host", host.Host, "--port", "8095", "--name", "envapp"];

        var exit = await command.ExecuteAsync([.. args, "--env", "DB_PASSWORD=hunter2"], CancellationToken.None);
        Assert.True(exit == 0, $"deploy with --env failed.\n{console.ErrorText}");

        // docker really did apply the env file: the value is live inside the container.
        var (_, value) = await host.DockerAsync("exec", "envapp", "printenv", "DB_PASSWORD");
        Assert.Equal("hunter2", value.Trim());

        // ...and the very next deploy, without it, is refused rather than starting the app unconfigured.
        var second = new StringConsole();
        var again = await Command(project, second).ExecuteAsync(args, CancellationToken.None);

        Assert.Equal(1, again);
        Assert.Contains("DB_PASSWORD", second.ErrorText, StringComparison.Ordinal);

        // The old container is untouched — a refusal changes nothing.
        var (_, still) = await host.DockerAsync("exec", "envapp", "printenv", "DB_PASSWORD");
        Assert.Equal("hunter2", still.Trim());
    }

    /// <summary>An app whose body identifies which build is serving.</summary>
    private static string AppWithMarker(string marker) =>
        $$"""
        FROM docker.io/library/nginx:alpine
        RUN mkdir -p /data \
         && printf 'server { listen 8080; location /health { return 200 "{{marker}}"; } }\n' > /etc/nginx/conf.d/default.conf
        EXPOSE 8080
        CMD ["/bin/sh","-c","echo boot >> /data/boots.txt; exec nginx -g 'daemon off;'"]
        """.ReplaceLineEndings("\n");

    /// <summary>What the app is actually serving right now, fetched from inside the host.</summary>
    private async Task<string> ServedBodyAsync(string slug)
    {
        var (_, container) = await host.DockerAsync("ps", "--filter", $"label=rask.app={slug}", "--format", "{{.Names}}");
        var (_, body) = await host.DockerAsync("exec", container.Trim(), "wget", "-qO-", "http://127.0.0.1:8080/health");
        return body.Trim();
    }

    /// <summary>A second command against the same project directory (each verb gets a fresh console).</summary>
    private DeployCommand Command(string project, StringConsole console) =>
        new(console, new SystemFileSystem(), new EnvScopedProcessRunner(host.Environment, host.Executables), project)
        {
            ReadinessDelay = TimeSpan.FromMilliseconds(500),
            ReadinessAttempts = 30,
        };

    /// <summary>
    /// Set up a project directory holding the app's Dockerfile and a <see cref="DeployCommand"/> wired to the
    /// real filesystem and a process runner scoped to the fixture's ssh identity. Skips when the gate is off
    /// or the fake VPS couldn't start, so a machine without Docker reports SKIPPED rather than failing.
    /// </summary>
    private string Start(out StringConsole console, out DeployCommand command)
    {
        Skip.IfNot(DeployE2E.Enabled, DeployE2E.SkipReason);

        // Deliberately a FAILURE, not another skip: the gate was explicitly asked for with
        // RASK_DEPLOY_E2E=1, so "the harness couldn't start" is a result the runner must see. Skipping
        // here would recreate exactly the fail-open hole the CLI build gates just had — a suite that
        // reports fine while verifying nothing.
        Assert.True(host.Unavailable is null, $"the deploy host gate was requested but its host is unusable — {host.Unavailable}");

        var project = Path.Combine(Path.GetTempPath(), "rask-deploy-e2e", "proj-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(project);
        File.WriteAllText(Path.Combine(project, "Dockerfile"), AppDockerfile.ReplaceLineEndings("\n"));

        console = new StringConsole();
        command = new DeployCommand(console, new SystemFileSystem(), new EnvScopedProcessRunner(host.Environment, host.Executables), project)
        {
            // The app answers immediately; a real box's 2s×10 budget only slows the gate down.
            ReadinessDelay = TimeSpan.FromMilliseconds(500),
            ReadinessAttempts = 30,
        };
        return project;
    }

    private static int CountBoots(string output) =>
        output.Split('\n').Count(l => l.Trim() == "boot");
}
