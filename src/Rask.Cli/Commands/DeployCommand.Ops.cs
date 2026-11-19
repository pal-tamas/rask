using System.Globalization;
using Spectre.Console;
using static Rask.Cli.Commands.CaddyRouting;
using static Rask.Cli.Commands.DockerCommands;

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
        if (logsOnly && !string.Equals(action, "logs", StringComparison.Ordinal))
        {
            error = "--tail and --follow only apply to `rask deploy logs`.";
            return false;
        }

        if (action is null)
        {
            return true;
        }

        // Everything below changes what would be deployed, which none of these verbs do.
        if (Array.Find(["domain", "redirect", "dns", "dns-env-file", "port", "container-port", "dockerfile", "health-path"], name => parsed.Option(name) is not null || parsed.MultiOption(name).Count > 0)
            is { } option)
        {
            error = $"--{option} doesn't apply to `rask deploy {action}` — it operates on what's already deployed.";
            return false;
        }

        if (Array.Find(["github-actions", "dry-run", "setup-host", "no-prune"], parsed.HasFlag) is { } flag)
        {
            error = $"--{flag} doesn't apply to `rask deploy {action}`.";
            return false;
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
        if (listing.ExitCode != 0)
        {
            // An unreachable host or daemon lists nothing, and "nothing deployed" would be a wrong answer.
            return Unread(host, "what runs there", listing);
        }

        var apps = ParseStatusRows(listing.StandardOutput);

        // Before the empty-list branch: "nothing is deployed" is an answer a script wants as an empty
        // array, not as prose on stdout that it then has to recognise.
        if (asJson)
        {
            var disk = await ReadDiskStatusAsync(host, cancellationToken).ConfigureAwait(false);
            JsonOutput.Write(
                Console,
                new DeployStatusReport(HostName(host), [.. apps.Select(a => ToJson(a, slug))], disk),
                CliJsonContext.Default.DeployStatusReport);
            return 0;
        }

        if (apps.Count == 0)
        {
            Console.WriteLine($"Nothing deployed on {HostName(host)} yet.", ConsoleStyle.Dim);
            await Console.Out.WriteLineAsync("Run `rask deploy` to ship this app.", cancellationToken).ConfigureAwait(false);
            return 0;
        }

        WriteHeading($"Deployed on {HostName(host)}");
        Console.Ansi.Write(new RaggedRight(new Padder(StatusTable(apps, slug), new Padding(2, 0, 0, 0))));

        // Whether a rollback is even possible is the other half of "what's the state of this app".
        await Console.Out.WriteLineAsync(cancellationToken).ConfigureAwait(false);
        var rollbackTo = await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (rollbackTo.Failure is { } failure)
        {
            return Unread(host, $"{slug}:{PreviousTag}", failure);
        }

        Console.WriteLine(
            rollbackTo.Image is null
                ? $"  No previous image for '{slug}' — `rask deploy rollback` has nothing to go back to yet."
                : $"  `rask deploy rollback` would restore {slug}:{PreviousTag} ({rollbackTo.Image}).",
            ConsoleStyle.Dim);
        WriteDiskStatus(host, await ReadDiskStatusAsync(host, cancellationToken).ConfigureAwait(false));

        return 0;
    }

    private static Table StatusTable(IEnumerable<StatusRow> apps, string slug)
    {
        var rows = apps.Select(a => (
            App: a.App.Length > 0 ? a.App : a.Container,
            Where: Address(a),
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

        return table;
    }

    /// <summary>Where an app answers: its domain, else its published ports.</summary>
    private static string Address(StatusRow app)
    {
        if (app.Domain.Length > 0)
        {
            return string.Join(", ", SiteDomains.Hosts(app.Domain).Select(host => $"https://{host}"));
        }

        return app.Ports.Length > 0 ? app.Ports : "(not published)";
    }

    // ── logs ────────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Tail the live container's logs. Runs with the streams inherited rather than captured, so
    /// <c>--follow</c> streams to the terminal and Ctrl-C ends it the way it would for plain docker.
    /// </summary>
    private async Task<int> LogsAsync(string host, string slug, ParsedArguments parsed, CancellationToken cancellationToken)
    {
        var listing = await Capture(BuildListArguments(host), cancellationToken).ConfigureAwait(false);
        if (listing.ExitCode != 0)
        {
            return Unread(host, "what runs there", listing);
        }

        var container = LiveContainer(listing, slug);
        if (container is null)
        {
            Console.WriteErrorLine($"'{slug}' isn't running on {HostName(host)} — nothing to show logs for.", ConsoleStyle.Error);
            await Console.Error.WriteLineAsync("Run `rask deploy status` to see what is deployed.", cancellationToken).ConfigureAwait(false);
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
        IReadOnlyList<string> env, bool healthEnabled, string healthPath, CancellationToken cancellationToken, string redirects = "")
    {
        var lookup = await ResolveRollbackImageAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (lookup.Failure is { } failure)
        {
            return Unread(host, $"{slug}:{PreviousTag}", failure);
        }

        if (lookup.Image is not { } image)
        {
            Console.WriteErrorLine($"No previous image for '{slug}' on {HostName(host)} — nothing to roll back to.", ConsoleStyle.Error);
            Console.WriteErrorLine(
                "A rollback goes back to the version before the current one, and this app has been deployed only once. Deploy a fixed version instead.",
                ConsoleStyle.Error);
            return 1;
        }

        WriteHeading($"Rolling {slug} back to {slug}:{PreviousTag} ({image})…");

        // persist: false — a rollback changes which image is running, not how the app is configured. Left
        // on, the nulls passed for project/envFile below would erase those keys from .rask/deploy.json.
        var result = domain is null
            ? await DeployPortAsync(host, slug, port, containerPort, env, project: null, envFile: null, healthEnabled, healthPath, cancellationToken, tag: PreviousTag, persist: false).ConfigureAwait(false)
            : await DeployWithProxyAsync(host, slug, domain, containerPort, env, project: null, envFile: null, healthEnabled, healthPath, cancellationToken, tag: PreviousTag, persist: false, redirects: redirects).ConfigureAwait(false);

        if (result != 0)
        {
            return result;
        }

        // Swap the tags so :current still names what is serving. Without this a second `rask deploy
        // rollback` would "roll back" to the image it just restored — and a later deploy would file the
        // rolled-back image away as the previous version, losing the one that was actually replaced.
        // Still under the app's lock (RunActionAsync), so no deploy of this app moves a tag in between.
        if (!await SwapTagsAsync(host, slug, cancellationToken).ConfigureAwait(false))
        {
            return 1;
        }

        Console.WriteLine("Rolled back. `rask deploy rollback` again to undo this.", ConsoleStyle.Dim);
        return 0;
    }

    /// <summary>
    /// Exchange <c>:current</c> and <c>:previous</c> via a scratch tag, then drop the scratch. False, said why, when a
    /// step failed: the next deploy files <c>:current</c> away as <c>:previous</c>, so a half-swapped pair would lose a
    /// version, and the operator has to know.
    /// </summary>
    private async Task<bool> SwapTagsAsync(string host, string slug, CancellationToken cancellationToken)
    {
        const string scratch = "rollback-swap";
        (string From, string To)[] moves = [(CurrentTag, scratch), (PreviousTag, CurrentTag), (scratch, PreviousTag)];
        foreach (var (from, to) in moves)
        {
            if (await Run(BuildRetagArguments(host, slug, from, to), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine(
                    $"The previous version is serving, but its image tags could not be swapped: `docker tag {slug}:{from} {slug}:{to}` failed, "
                    + $"so {slug}:{CurrentTag} may not name what serves. Put {slug}:{CurrentTag} on the serving image and {slug}:{PreviousTag} on the one "
                    + "it replaced before the next deploy or rollback.",
                    ConsoleStyle.Error);
                return false;
            }
        }

        // Best effort: removing a tag that shares an image with others only untags it, and a scratch tag left behind
        // is overwritten by the next swap.
        await Run(BuildUntagArguments(host, slug, scratch), cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// The image id behind <c>:previous</c> — none when there isn't one, and a failure when docker could not be asked:
    /// an unreachable host is no answer to "is there a version to go back to".
    /// </summary>
    private async Task<ImageLookup> ResolveRollbackImageAsync(string host, string slug, CancellationToken cancellationToken)
    {
        var result = await Capture(BuildImageExistsArguments(host, slug, PreviousTag), cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return result.StandardError.Contains("No such image", StringComparison.OrdinalIgnoreCase)
                ? new ImageLookup(null, null)
                : new ImageLookup(null, result);
        }

        var id = result.StandardOutput.Trim();
        return new ImageLookup(id.Length == 0 ? null : Shorten(id), null);
    }

    /// <summary>What <see cref="ResolveRollbackImageAsync"/> found: an image, none, or the docker call that failed.</summary>
    private readonly record struct ImageLookup(string? Image, ProcessResult? Failure);

    /// <summary>The container currently serving this app, from its labels (blue/green, or port-mode).</summary>
    private static string? LiveContainer(ProcessResult listing, string slug) =>
        ParseDeployedApps(listing.StandardOutput).FirstOrDefault(a => string.Equals(a.App, slug, StringComparison.Ordinal)).Container is { Length: > 0 } container
            ? container
            : null;

    /// <summary>Says that <paramref name="what"/> on the host could not be read, with docker's own words; 1.</summary>
    private int Unread(string host, string what, ProcessResult result)
    {
        var detail = result.StandardError.Trim();
        Console.WriteErrorLine(
            $"Couldn't read {what} on {HostName(host)} (docker exited {result.ExitCode.ToString(CultureInfo.InvariantCulture)})"
            + (detail.Length > 0 ? $": {detail}" : "."),
            ConsoleStyle.Error);
        return 1;
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

        static string Label(string value) => string.Equals(value, "<no value>", StringComparison.Ordinal) ? string.Empty : value.Trim();
    }
}
