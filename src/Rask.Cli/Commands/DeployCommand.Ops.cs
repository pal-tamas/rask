using System.Globalization;
using Rask.Cli.Scaffolding;
using Spectre.Console;

namespace Rask.Cli.Commands;

/// <summary>
/// The day-two half of <c>rask deploy</c>: what you need <em>after</em> the app is live.
///
/// <para>Shipping was already covered; operating wasn't. Seeing what's running, reading its logs, and
/// undoing a bad release all meant hand-writing <c>docker -H ssh://…</c> commands — which is exactly the
/// SSH session the deploy story promises you never have to open. These verbs read the same
/// <c>rask.*</c> container labels the deploy writes, so they describe the box as it actually is rather
/// than as <c>.rask/deploy.json</c> remembers it.</para>
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>Default log lines to show — enough to see a startup failure, short enough to read.</summary>
    private const int DefaultLogTail = 100;

    /// <summary>
    /// Reject options that belong to a different verb, rather than accepting and ignoring them. Silently
    /// dropping <c>--follow</c> from a deploy, or <c>--domain</c> from a rollback, would leave the user
    /// believing something happened that didn't.
    /// </summary>
    private static bool TryRejectMisplacedOptions(ParsedArguments parsed, string? action, out string? error)
    {
        error = null;

        var logsOnly = parsed.Option("tail") is not null || parsed.HasFlag("follow");
        if (logsOnly && action != "logs")
        {
            error = "--tail and --follow only apply to `rask deploy logs`.";
            return false;
        }

        if (action is null)
        {
            return true;
        }

        // Everything below changes what would be deployed, which none of these verbs do.
        foreach (var name in new[] { "domain", "port", "container-port", "dockerfile", "health-path" })
        {
            if (parsed.Option(name) is not null)
            {
                error = $"--{name} doesn't apply to `rask deploy {action}` — it operates on what's already deployed.";
                return false;
            }
        }

        foreach (var name in new[] { "github-actions", "dry-run", "setup-host" })
        {
            if (parsed.HasFlag(name))
            {
                error = $"--{name} doesn't apply to `rask deploy {action}`.";
                return false;
            }
        }

        return true;
    }

    // ── status ──────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// What is actually running on the box, read from the containers themselves. Lists every Rask-managed
    /// app, not just this one: they share a host and a proxy, so "what else is here" is part of the
    /// answer — and it's how you notice a second app you'd forgotten holding port 80.
    /// </summary>
    private async Task<int> StatusAsync(string host, string slug, bool asJson, CancellationToken cancellationToken)
    {
        var listing = await Capture(BuildStatusArguments(host), cancellationToken).ConfigureAwait(false);
        var apps = ParseStatusRows(listing.StandardOutput);

        // Before the empty-list branch: "nothing is deployed" is an answer a script wants as an empty
        // array, not as prose on stdout that it then has to recognise.
        if (asJson)
        {
            JsonOutput.Write(
                Console,
                new DeployStatusReport(HostName(host), [.. apps.Select(a => ToJson(a, slug))]),
                CliJsonContext.Default.DeployStatusReport);
            return 0;
        }

        if (apps.Count == 0)
        {
            Console.WriteLine($"Nothing deployed on {HostName(host)} yet.", ConsoleStyle.Dim);
            Console.Out.WriteLine("Run `rask deploy` to ship this app.");
            return 0;
        }

        WriteHeading($"Deployed on {HostName(host)}");

        var rows = apps.Select(a => (
            App: a.App.Length > 0 ? a.App : a.Container,
            Where: a.Domain.Length > 0 ? $"https://{a.Domain}" : a.Ports.Length > 0 ? a.Ports : "(not published)",
            Colour: a.Color.Length > 0 ? a.Color : "-",
            State: a.Status)).ToArray();

        var table = new Table()
            .Border(TableBorder.None)
            .HideHeaders()
            .AddColumns("app", "url", "colour", "state");

        table.AddRow(
            new Text("APP", ConsoleStyling.Of(ConsoleStyle.Dim)),
            new Text("URL", ConsoleStyling.Of(ConsoleStyle.Dim)),
            new Text("COLOUR", ConsoleStyling.Of(ConsoleStyle.Dim)),
            new Text("STATE", ConsoleStyling.Of(ConsoleStyle.Dim)));

        foreach (var row in rows)
        {
            // This project's own app is highlighted; the others are context, and printing them plain
            // keeps it obvious at a glance which row you came here about.
            var style = string.Equals(row.App, slug, StringComparison.Ordinal)
                ? ConsoleStyling.Of(ConsoleStyle.Success)
                : Style.Plain;

            table.AddRow(
                new Text(row.App, style), new Text(row.Where, style),
                new Text(row.Colour, style), new Text(row.State, style));
        }

        Console.Ansi.Write(new RaggedRight(new Padder(table, new Padding(2, 0, 0, 0))));

        // Whether a rollback is even possible is the other half of "what's the state of this app".
        Console.Out.WriteLine();
        var rollbackTo = await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false);
        Console.WriteLine(
            rollbackTo is null
                ? $"  No previous image for '{slug}' — `rask deploy rollback` has nothing to go back to yet."
                : $"  `rask deploy rollback` would restore {slug}:{PreviousTag} ({rollbackTo}).",
            ConsoleStyle.Dim);

        return 0;
    }

    // ── logs ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tail the live container's logs. Runs with the streams inherited rather than captured, so
    /// <c>--follow</c> streams to the terminal and Ctrl-C ends it the way it would for plain docker.
    /// </summary>
    private async Task<int> LogsAsync(string host, string slug, ParsedArguments parsed, CancellationToken cancellationToken)
    {
        var container = await ResolveLiveContainerAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (container is null)
        {
            Console.WriteErrorLine($"'{slug}' isn't running on {HostName(host)} — nothing to show logs for.", ConsoleStyle.Error);
            Console.Error.WriteLine("Run `rask deploy status` to see what is deployed.");
            return 1;
        }

        if (!TryResolveTail(parsed.Option("tail"), out var tail, out var tailError))
        {
            return Fail(tailError!);
        }

        return await Run(BuildLogsArguments(host, container, tail, parsed.HasFlag("follow")), cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>--tail</c> is a line count or the literal <c>all</c>, matching <c>docker logs</c>.</summary>
    private static bool TryResolveTail(string? value, out string tail, out string? error)
    {
        error = null;
        if (value is null)
        {
            tail = DefaultLogTail.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (string.Equals(value, "all", StringComparison.OrdinalIgnoreCase))
        {
            tail = "all";
            return true;
        }

        if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var lines) && lines > 0)
        {
            tail = lines.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        tail = string.Empty;
        error = $"--tail must be a positive number or 'all', not '{value}'.";
        return false;
    }

    // ── rollback ────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Put the previous image back, through the same gates a deploy goes through.
    ///
    /// <para>The deploy-time rollback only covers a release that <em>fails</em> — a container that won't
    /// start, or won't answer. It can't help with the worse case: a release that starts, answers, and is
    /// simply wrong. That is what this is for, and it is deliberately the same code path as a deploy
    /// (start alongside, gate on running + healthy, reload the proxy, retire the old one) so a rollback
    /// can't take traffic on weaker evidence than the deploy it's undoing.</para>
    /// </summary>
    private async Task<int> RollbackAsync(
        string host, string slug, string? domain, int port, int containerPort,
        IReadOnlyList<string> env, bool healthEnabled, string healthPath, CancellationToken cancellationToken)
    {
        var image = await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (image is null)
        {
            Console.WriteErrorLine($"No previous image for '{slug}' on {HostName(host)} — nothing to roll back to.", ConsoleStyle.Error);
            Console.WriteErrorLine(
                $"A rollback restores {slug}:{PreviousTag}, which is written by the deploy that replaces it. The first deploy of an app has no predecessor.",
                ConsoleStyle.Error);
            return 1;
        }

        WriteHeading($"Rolling {slug} back to {slug}:{PreviousTag} ({image})…");

        // persist: false — a rollback changes which image is running, not how the app is configured. Left
        // on, the nulls passed for project/envFile below would erase those keys from .rask/deploy.json.
        var result = domain is null
            ? await DeployPortAsync(host, slug, port, containerPort, env, project: null, envFile: null, healthEnabled, healthPath, cancellationToken, tag: PreviousTag, persist: false).ConfigureAwait(false)
            : await DeployWithProxyAsync(host, slug, domain, containerPort, env, project: null, envFile: null, healthEnabled, healthPath, cancellationToken, tag: PreviousTag, persist: false).ConfigureAwait(false);

        if (result != 0)
        {
            return result;
        }

        // Swap the tags so :current still names what is serving. Without this a second `rask deploy
        // rollback` would "roll back" to the image it just restored — and a later deploy would file the
        // rolled-back image away as the previous version, losing the one that was actually replaced.
        await SwapTagsAsync(host, slug, cancellationToken).ConfigureAwait(false);

        Console.WriteLine("Rolled back. `rask deploy rollback` again to undo this.", ConsoleStyle.Dim);
        return 0;
    }

    /// <summary>Exchange <c>:current</c> and <c>:previous</c> via a scratch tag, then drop the scratch.</summary>
    private async Task SwapTagsAsync(string host, string slug, CancellationToken cancellationToken)
    {
        const string scratch = "rollback-swap";
        await Run(BuildRetagArguments(host, slug, CurrentTag, scratch), cancellationToken).ConfigureAwait(false);
        await Run(BuildRetagArguments(host, slug, PreviousTag, CurrentTag), cancellationToken).ConfigureAwait(false);
        await Run(BuildRetagArguments(host, slug, scratch, PreviousTag), cancellationToken).ConfigureAwait(false);

        // Removing a tag that shares an image with others only untags it — the image itself survives.
        await Run(BuildUntagArguments(host, slug, scratch), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The image id behind <c>:previous</c>, or null when there isn't one.</summary>
    private async Task<string?> ResolveRollbackImageAsync(string host, string slug, CancellationToken cancellationToken)
    {
        var result = await Capture(BuildImageExistsArguments(host, slug, PreviousTag), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        var id = result.StandardOutput.Trim();
        return id.Length == 0 ? null : Shorten(id);
    }

    /// <summary>The container currently serving this app, from its labels (blue/green, or port-mode).</summary>
    private async Task<string?> ResolveLiveContainerAsync(string host, string slug, CancellationToken cancellationToken)
    {
        var listing = await Capture(BuildListArguments(host), cancellationToken).ConfigureAwait(false);
        var apps = ParseDeployedApps(listing.StandardOutput);
        return apps.FirstOrDefault(a => string.Equals(a.App, slug, StringComparison.Ordinal)).Container is { Length: > 0 } container
            ? container
            : null;
    }

    /// <summary>"sha256:8f1a…" → "8f1a…" — an image id is only ever shown to identify a version.</summary>
    private static string Shorten(string imageId)
    {
        var bare = imageId.StartsWith("sha256:", StringComparison.Ordinal) ? imageId["sha256:".Length..] : imageId;
        return bare.Length > 12 ? bare[..12] : bare;
    }

    // ── Pure builders/parsers for the ops verbs ─────────────────────────────────────────────────────

    /// <summary>
    /// A richer listing than <see cref="BuildListArguments"/>: that one feeds the routing map and must stay
    /// exactly the four fields it parses, so status asks its own question rather than widening it.
    /// </summary>
    internal static IReadOnlyList<string> BuildStatusArguments(string host) =>
    [
        .. Prefix(host), "ps", "--filter", "label=rask.managed=true",
        "--format", "{{.Names}}\t{{.Label \"rask.app\"}}\t{{.Label \"rask.domain\"}}\t{{.Label \"rask.color\"}}\t{{.Status}}\t{{.Ports}}",
    ];

    internal static IReadOnlyList<string> BuildUntagArguments(string host, string slug, string tag) =>
        [.. Prefix(host), "image", "rm", $"{slug}:{tag}"];

    /// <summary>
    ///     One status row as JSON. The human table folds several things together for width — an empty
    ///     app name falls back to the container, a missing domain becomes the ports or "(not published)"
    ///     — which is right for reading and wrong for parsing, so the fields stay separate here and empty
    ///     strings become null rather than pretending to be values.
    /// </summary>
    private static DeployedAppStatus ToJson(StatusRow row, string slug)
    {
        var app = row.App.Length > 0 ? row.App : row.Container;
        return new DeployedAppStatus(
            app,
            row.Container,
            Nullify(row.Domain),
            Nullify(row.Ports),
            Nullify(row.Color),
            row.Status,
            string.Equals(app, slug, StringComparison.Ordinal));

        static string? Nullify(string value) => value.Length == 0 ? null : value;
    }

    /// <summary>Parse the status listing. Malformed rows are skipped, never guessed at.</summary>
    internal static IReadOnlyList<StatusRow> ParseStatusRows(string psOutput)
    {
        var rows = new List<StatusRow>();
        foreach (var raw in psOutput.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split('\t');
            if (parts.Length < 6 || parts[0].Length == 0)
            {
                continue;
            }

            rows.Add(new StatusRow(parts[0], Label(parts[1]), Label(parts[2]), Label(parts[3]), Label(parts[4]), Label(parts[5])));
        }

        return rows;

        static string Label(string value) => value == "<no value>" ? string.Empty : value.Trim();
    }
}

/// <summary>One row of <c>rask deploy status</c>, as the host reported it.</summary>
internal readonly record struct StatusRow(string Container, string App, string Domain, string Color, string Status, string Ports);
