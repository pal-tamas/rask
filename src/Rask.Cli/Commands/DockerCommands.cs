using System.Globalization;
using static Rask.Cli.Commands.DeployCommand;
using static Rask.Cli.Commands.DeployEnvironment;

namespace Rask.Cli.Commands;

/// <summary>The <c>docker</c> argument lists a deploy runs against a remote host, built without side effects.</summary>
internal static class DockerCommands
{
    /// <summary>
    /// Build the image. Tagged <c>:current</c> (what containers are started from, and what the next deploy
    /// will move aside to <c>:previous</c>) and <c>:latest</c>, which is kept purely so the box still reads
    /// the way a person expects when they run <c>docker images</c> themselves.
    /// </summary>
    internal static IReadOnlyList<string> BuildBuildArguments(string host, string slug, string dockerfile, string contextDir) =>
        [.. Prefix(host), "build", "-t", $"{slug}:{CurrentTag}", "-t", $"{slug}:latest", "-f", dockerfile, contextDir];

    /// <summary>Move the live image aside before a build overwrites its tag, so it can be rolled back to.</summary>
    internal static IReadOnlyList<string> BuildRetagArguments(string host, string slug, string from, string to) =>
        [.. Prefix(host), "tag", $"{slug}:{from}", $"{slug}:{to}"];

    /// <summary>Does this tag exist on the host? Used to tell "nothing to roll back to" from a failure.</summary>
    internal static IReadOnlyList<string> BuildImageExistsArguments(string host, string slug, string tag) =>
        [.. Prefix(host), "image", "inspect", "--format", "{{.Id}}", $"{slug}:{tag}"];

    internal static IReadOnlyList<string> BuildNetworkCreateArguments(string host, string network) =>
        [.. Prefix(host), "network", "create", network];

    /// <summary>
    /// The <c>docker run</c> for an app container. Runtime environment goes in through
    /// <paramref name="envFilePath"/> when there is one: <c>--env-file</c> is read by the local docker CLI,
    /// so the values never appear in this machine's process table the way <c>-e KEY=VALUE</c> does. Entries
    /// whose value spans lines can't be expressed in that format and stay inline.
    /// </summary>
    internal static IReadOnlyList<string> BuildRunArguments(string host, string slug, string? domain, string? color, int port, IReadOnlyList<string> env, int containerPort = DefaultContainerPort, string tag = CurrentTag, string? envFilePath = null)
    {
        var args = new List<string>(Prefix(host)) { "run", "-d" };

        // Container-runtime hygiene for a box that is expected to run unattended for months:
        //  • json-file logs are unbounded by default, and a chatty app filling the disk takes the whole
        //    host down with it — including every other app sharing the box.
        //  • no-new-privileges stops a compromised process gaining rights via setuid binaries. It costs
        //    nothing here: nothing a Rask app does needs to escalate.
        args.AddRange(["--log-opt", "max-size=10m", "--log-opt", "max-file=3", "--security-opt", "no-new-privileges"]);
        AddPlacementArguments(args, slug, domain, color, port, containerPort);

        // Persist the SQLite database on a per-app named volume so it survives container replacement — every
        // deploy runs a fresh container, and without this the DB (in the container's writable layer) would be
        // destroyed on every redeploy. Point the app at it via Rask:ConnectionStrings:App, which every Rask
        // database battery reads; the volume is shared by both blue/green colors so the swap keeps the same database. A
        // user-supplied --env / --env-file value is appended after, so an explicit override still wins.
        // ASPNETCORE_ENVIRONMENT is what selects appsettings.Production.json and turns off the developer
        // exception page; relying on the base image's default left a deployed app in whatever environment
        // the image happened to assume. Set before the user's own --env, so an explicit override still wins.
        args.AddRange(["-e", "ASPNETCORE_ENVIRONMENT=Production"]);

        args.AddRange(["-v", $"{slug}-data:/data", "-e", "Rask__ConnectionStrings__App=Data Source=/data/app.db"]);

        // The log store keeps a file of its own, so it needs its own pointer onto the same volume — without
        // this it would land in the container's writable layer and be destroyed by the very restart it
        // exists to survive. Harmless on an app that doesn't use Rask.Logging: nothing reads the value.
        args.AddRange(["-e", "Rask__ConnectionStrings__Logs=Data Source=/data/logs.db"]);

        if (envFilePath is not null)
        {
            args.AddRange(["--env-file", envFilePath]);
        }

        // With an env file, only the entries it cannot carry are still passed inline.
        foreach (var entry in env.Where(entry => envFilePath is null || !CanGoInEnvFile(entry)))
        {
            args.AddRange(["-e", entry]);
        }

        args.Add($"{slug}:{tag}");
        return args;
    }

    // Port mode publishes the container straight to the host; domain mode puts it on the proxy's network as
    // one color of a blue/green pair, labelled so the Caddyfile can be regenerated from the live containers.
    private static void AddPlacementArguments(List<string> args, string slug, string? domain, string? color, int port, int containerPort)
    {
        if (domain is null)
        {
            args.AddRange(["--name", slug, "--restart", "unless-stopped", "-p", $"{port}:{containerPort}"]);

            // Labelled like a domain-mode container (minus a domain, so BuildRoutingMap skips it): without
            // this a port-mode deploy is invisible to the host inventory, so switching an app to --domain
            // would strand its old container running forever.
            args.AddRange(["--label", "rask.managed=true", "--label", $"rask.app={slug}", "--label", $"rask.port={containerPort.ToString(CultureInfo.InvariantCulture)}"]);
        }
        else
        {
            args.AddRange(["--name", $"{slug}-{color}", "--restart", "unless-stopped", "--network", Network]);
            args.AddRange(["--label", "rask.managed=true", "--label", $"rask.app={slug}", "--label", $"rask.domain={domain}", "--label", $"rask.color={color}"]);
            args.AddRange(["--label", $"rask.port={containerPort.ToString(CultureInfo.InvariantCulture)}"]);

            // Caddy terminates TLS in front of this container, so the app trusts its forwarded headers — without
            // this Request.Scheme is "http", HSTS never emits and every visitor has the proxy's address. Only in
            // this mode: a port-mode container is reached directly, where trusting them lets a client forge its IP.
            args.AddRange(["-e", "Rask__BehindProxy=true"]);

            // The address emailed links (confirm, reset) point at. Outside Development the app will not take it
            // from the request, whose Host header anyone can set, so without this no such email goes out.
            args.AddRange(["-e", $"Rask__Auth__PublicOrigin=https://{domain}"]);
        }
    }

    /// <summary>
    /// Gracefully stop a container: SIGTERM, then SIGKILL after <see cref="StopTimeoutSeconds"/>. Used before
    /// retiring a container that's serving, so SQLite checkpoints the WAL cleanly before exit — and, when a
    /// replica is configured, the in-process Litestream replicator flushes. A plain <c>rm -f</c> (SIGKILL)
    /// would lose the last frames.
    /// </summary>
    internal static IReadOnlyList<string> BuildStopArguments(string host, string container) =>
        [.. Prefix(host), "stop", "-t", StopTimeoutSeconds.ToString(CultureInfo.InvariantCulture), container];

    internal static IReadOnlyList<string> BuildCaddyRunArguments(string host, string network) =>
    [
        .. Prefix(host), "run", "-d", "--name", CaddyContainer, "--restart", "unless-stopped",
        "--network", network, "-p", "80:80", "-p", "443:443", "-v", "rask-caddy-data:/data", "caddy:2",
    ];

    internal static IReadOnlyList<string> BuildListArguments(string host) =>
    [
        .. Prefix(host), "ps", "--filter", "label=rask.managed=true",
        "--format", "{{.Names}}\t{{.Label \"rask.app\"}}\t{{.Label \"rask.domain\"}}\t{{.Label \"rask.color\"}}\t{{.Label \"rask.port\"}}",
    ];

    internal static IReadOnlyList<string> BuildInspectRunningArguments(string host, string container) =>
        [.. Prefix(host), "inspect", "--format", "{{.State.Running}}", container];

    /// <summary>
    /// An ephemeral <c>curl</c> container joined to <paramref name="container"/>'s network namespace, hitting
    /// the app on <c>localhost:8080</c>. <c>--rm</c> cleans it up; <c>-f</c> makes a <c>&gt;= 400</c> response a
    /// non-zero exit; <c>-m 5</c> bounds a hung connect. Exit 0 ⇒ the app answered a success status.
    /// </summary>
    internal static IReadOnlyList<string> BuildHealthCheckArguments(string host, string container, string healthPath, int containerPort = DefaultContainerPort) =>
    [
        .. Prefix(host), "run", "--rm", "--network", $"container:{container}", CurlImage,
        "-fsS", "-m", "5", $"http://localhost:{containerPort}{healthPath}",
    ];

    internal static IReadOnlyList<string> BuildLogsArguments(string host, string container, string tail = "50", bool follow = false)
    {
        var args = new List<string>(Prefix(host)) { "logs", "--tail", tail };
        if (follow)
        {
            args.Add("--follow");
        }

        args.Add(container);
        return args;
    }

    internal static IReadOnlyList<string> BuildRemoveArguments(string host, string container) =>
        [.. Prefix(host), "rm", "-f", container];

    internal static IReadOnlyList<string> BuildCaddyCopyArguments(string host, string localCaddyfile) =>
        [.. Prefix(host), "cp", localCaddyfile, $"{CaddyContainer}:/etc/caddy/Caddyfile"];

    internal static IReadOnlyList<string> BuildCaddyReloadArguments(string host) =>
        [.. Prefix(host), "exec", CaddyContainer, "caddy", "reload", "--config", "/etc/caddy/Caddyfile", "--adapter", "caddyfile"];

    internal static string[] Prefix(string host) => ["-H", $"ssh://{host}"];
}
