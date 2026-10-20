using static Rask.Cli.Commands.CaddyRouting;
using static Rask.Cli.Commands.DockerCommands;
using static Rask.Cli.Commands.ProxyCommands;

namespace Rask.Cli.Commands;

/// <summary>
/// The part of a domain deploy that touches what every app on the box shares — the network, the proxy and its
/// config — always under the box's deploy lock, and only ever writing this app's own config file.
/// </summary>
internal sealed partial class DeployCommand
{
    /// <summary>How long to wait between attempts at a deploy lock another deploy holds — zero in tests.</summary>
    internal TimeSpan LockPollDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>How many attempts at the deploy lock before giving up (past a full lease by default).</summary>
    internal int LockAttempts { get; set; } = (LockLeaseSeconds / 3) + 20;

    /// <summary>What a proxy switch left behind: routed to the new colour, the old colour still routed, or unsure (said why).</summary>
    private enum SwitchOutcome
    {
        Routed,
        NotRouted,
        Stuck,
    }

    /// <summary>One app's switch of the shared proxy to <see cref="Target"/>, its new colour.</summary>
    private sealed record ProxySwitch(string Host, string Slug, string Domain, RouteTarget Target, ProxyPlan Proxy, bool HealthEnabled, string HealthPath);

    /// <summary>What to put back when a switch fails: this app's file as it was (null: it had none), and the base Caddyfile when the switch replaced it.</summary>
    private sealed record SwitchUndo(string? Fragment, string? Base);

    /// <summary>Create the shared network if the box hasn't got it — under the lock, so two first deploys cannot create two.</summary>
    private async Task<bool> EnsureNetworkAsync(string host, string slug, CancellationToken cancellationToken)
    {
        if (await NetworkExistsAsync(host, cancellationToken).ConfigureAwait(false))
        {
            return true;
        }

        var deployLock = CreateLock();
        var held = await deployLock.AcquireAsync(host, slug, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return false;
        }

        try
        {
            if (await NetworkExistsAsync(host, cancellationToken).ConfigureAwait(false)
                || await Run(BuildNetworkCreateArguments(host, Network), cancellationToken).ConfigureAwait(false) == 0)
            {
                return true;
            }

            Console.WriteErrorLine($"Creating the '{Network}' docker network on {HostName(host)} failed — its output is above.", ConsoleStyle.Error);
            return false;
        }
        finally
        {
            await deployLock.ReleaseAsync(host, held).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Move the proxy to the new colour, or leave what served before serving. Not routed: the new colour never took
    /// traffic (or was taken off it again), so it is removed. Stuck: the proxy may still send traffic to it, so it
    /// stays — that was said, with what to check.
    /// </summary>
    private async Task<bool> RouteNewColorAsync(ProxySwitch change, CancellationToken cancellationToken)
    {
        // The DNS image builds for minutes, so before the lock; the proxy is replaced with it under the lock.
        var switched = change.Proxy.CreateWithCredentials is not null
            && !await EnsureDnsImageAsync(change.Host, change.Proxy.Provider!, cancellationToken).ConfigureAwait(false)
            ? SwitchOutcome.NotRouted
            : await SwitchProxyAsync(change, cancellationToken).ConfigureAwait(false);
        if (switched == SwitchOutcome.NotRouted)
        {
            await Run(BuildRemoveArguments(change.Host, change.Target.Container), cancellationToken).ConfigureAwait(false);
            Console.WriteErrorLine($"Removed {change.Target.Container}. The deploy failed; nothing that served before was changed.", ConsoleStyle.Error);
        }

        return switched == SwitchOutcome.Routed;
    }

    /// <summary>Point the proxy at the new colour, check it through the proxy, then retire the old colour — all under the lock.</summary>
    private async Task<SwitchOutcome> SwitchProxyAsync(ProxySwitch change, CancellationToken cancellationToken)
    {
        var deployLock = CreateLock();
        var held = await deployLock.AcquireAsync(change.Host, change.Slug, cancellationToken).ConfigureAwait(false);
        if (held is null)
        {
            return SwitchOutcome.NotRouted;
        }

        try
        {
            return await SwitchUnderLockAsync(change, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await deployLock.ReleaseAsync(change.Host, held).ConfigureAwait(false);
        }
    }

    private async Task<SwitchOutcome> SwitchUnderLockAsync(ProxySwitch change, CancellationToken cancellationToken)
    {
        var host = change.Host;
        var (ready, replacedBase) = await EnsureProxyAsync(host, change.Proxy, cancellationToken).ConfigureAwait(false);
        if (!ready)
        {
            return SwitchOutcome.NotRouted;
        }

        // Re-read inside the lock. The listing taken before the build is stale by now: another app may have swapped
        // colours since, and routing or retiring from that listing is how one deploy once broke another's routes.
        var apps = await ListAppsAsync(host, cancellationToken).ConfigureAwait(false);
        if (apps is null)
        {
            return SwitchOutcome.NotRouted;
        }

        if (await Run(BuildSitesDirectoryArguments(host), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine($"Creating {CaddySites.SitesDirectory} in the proxy failed — its output is above.", ConsoleStyle.Error);
            return SwitchOutcome.NotRouted;
        }

        return await RewriteRoutesAsync(change, apps, replacedBase, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Write this app's file (moving the box to one file per app first, if it isn't yet), reload, check, retire.</summary>
    private async Task<SwitchOutcome> RewriteRoutesAsync(ProxySwitch change, IReadOnlyList<DeployedApp> apps, string? replacedBase, CancellationToken cancellationToken)
    {
        var (host, slug) = (change.Host, change.Slug);

        // A box an older Rask deployed has one Caddyfile naming every app. It moves to one file per app here, the first
        // time any app deploys: the others are carried over from their running containers, then the base is swapped.
        var onDisk = await ReadProxyFileAsync(host, CaddySites.BasePath, cancellationToken).ConfigureAwait(false);
        var evidence = replacedBase ?? onDisk;
        var writeBase = !CaddySites.IsFragmentLayout(onDisk);
        var undo = new SwitchUndo(
            await ReadProxyFileAsync(host, CaddySites.FragmentPath(slug), cancellationToken).ConfigureAwait(false),
            writeBase ? evidence : null);

        if (!CaddySites.IsFragmentLayout(evidence)
            && !await CarryOverOtherAppsAsync(host, slug, apps, evidence ?? string.Empty, change.Proxy.Provider, cancellationToken).ConfigureAwait(false))
        {
            return SwitchOutcome.NotRouted;
        }

        await Console.Out.WriteLineAsync($"Routing {SiteDomains.CaddyAddress(change.Domain)} → {change.Target.Container} (auto-HTTPS via Caddy)…").ConfigureAwait(false);
        if (!await WriteProxyFileAsync(host, slug, CaddySites.FragmentPath(slug), BuildFragment(change.Domain, change.Target, change.Proxy.Provider), cancellationToken).ConfigureAwait(false))
        {
            return SwitchOutcome.NotRouted;
        }

        if (writeBase && !await WriteProxyFileAsync(host, slug, CaddySites.BasePath, CaddySites.BaseCaddyfile, cancellationToken).ConfigureAwait(false))
        {
            return await UndoAsync(change, undo, cancellationToken).ConfigureAwait(false);
        }

        await DropDeadAppsAsync(host, slug, apps, cancellationToken).ConfigureAwait(false);

        // Retry the reload: on a fresh host the proxy was just started detached, so its admin endpoint may
        // need a moment before `caddy reload` succeeds.
        if (await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) != 0)
        {
            Console.WriteErrorLine($"Caddy refused the new config — its output is above. Check `docker -H ssh://{host} logs {CaddyContainer}`.", ConsoleStyle.Error);
            return await UndoAsync(change, undo, cancellationToken).ConfigureAwait(false);
        }

        if (await VerifyRouteAsync(change, cancellationToken).ConfigureAwait(false) is { } seen)
        {
            Console.WriteErrorLine($"The proxy is not serving {SiteDomains.Primary(change.Domain)} from {change.Target.Container}: {seen}.", ConsoleStyle.Error);
            return await UndoAsync(change, undo, cancellationToken).ConfigureAwait(false);
        }

        await RetireOldColorsAsync(host, slug, apps, change.Target.Container, cancellationToken).ConfigureAwait(false);
        return SwitchOutcome.Routed;
    }

    /// <summary>
    /// Make sure the proxy is up: replaced with a DNS provider's image when the plan says so, started when the box has
    /// none. Returns the base Caddyfile of a proxy it replaced — the new one starts with the stock file, and what the
    /// old one routed is the evidence the switch carries over.
    /// </summary>
    private async Task<(bool Ready, string? ReplacedBase)> EnsureProxyAsync(string host, ProxyPlan proxy, CancellationToken cancellationToken)
    {
        if (proxy.CreateWithCredentials is not null)
        {
            return await ReplaceProxyAsync(host, proxy, cancellationToken).ConfigureAwait(false);
        }

        if (await ProxyExistsAsync(host, cancellationToken).ConfigureAwait(false))
        {
            return (true, null);
        }

        // Never recreated here, so the rask-caddy-data volume keeps every app's certificates and config files.
        if (await Run(BuildCaddyRunArguments(host, Network), cancellationToken).ConfigureAwait(false) == 0
            || await ProxyExistsAsync(host, cancellationToken).ConfigureAwait(false))
        {
            return (true, null);
        }

        Console.WriteErrorLine($"Starting the shared proxy ({CaddyContainer}) failed — its output is above. Left the previous version serving.", ConsoleStyle.Error);
        return (false, null);
    }

    /// <summary>
    /// Replace the shared proxy with one built with <paramref name="proxy"/>'s DNS provider — same name, network, ports
    /// and data volume, so every certificate and app file carries over — given the credentials. Replacing it interrupts
    /// every site on the box for the seconds Caddy takes to start, so it is said out loud first, and it happens once:
    /// the label it carries afterwards is what every later deploy keeps.
    /// </summary>
    private async Task<(bool Ready, string? ReplacedBase)> ReplaceProxyAsync(string host, ProxyPlan proxy, CancellationToken cancellationToken)
    {
        var dns = proxy.Provider!;
        string? replacedBase = null;
        if (await ProxyExistsAsync(host, cancellationToken).ConfigureAwait(false))
        {
            replacedBase = await ReadProxyFileAsync(host, CaddySites.BasePath, cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"Replacing the shared proxy with one that proves certificates over {dns.Name} DNS — every site on this box is down for the few seconds it takes to start…", ConsoleStyle.Warning);
            if (await Run(BuildRemoveArguments(host, CaddyContainer), cancellationToken).ConfigureAwait(false) != 0)
            {
                Console.WriteErrorLine("Removing the old proxy failed — its output is above. It was left as it is.", ConsoleStyle.Error);
                return (false, null);
            }
        }

        if (await Run(BuildCaddyRunArguments(host, Network, dns, proxy.CreateWithCredentials), cancellationToken).ConfigureAwait(false) == 0)
        {
            return (true, replacedBase);
        }

        // Never leave the box without a proxy: put the stock one back with the config the old one ran.
        Console.WriteErrorLine($"Starting the proxy with {dns.Image} failed — putting the stock {CaddyImage} proxy back.", ConsoleStyle.Error);
        await Run(BuildRemoveArguments(host, CaddyContainer), cancellationToken).ConfigureAwait(false); // ignore-absent
        var restored = await Run(BuildCaddyRunArguments(host, Network), cancellationToken).ConfigureAwait(false) == 0
            && (replacedBase is null
                || (await WriteProxyFileAsync(host, "base", CaddySites.BasePath, replacedBase, cancellationToken).ConfigureAwait(false)
                    && await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) == 0));
        if (!restored)
        {
            Console.WriteErrorLine($"Putting the stock proxy back failed too — the sites on this box may not be served. Check `docker -H ssh://{host} ps -a` and deploy again.", ConsoleStyle.Error);
        }

        return (false, null);
    }

    /// <summary>
    /// Build the proxy image with the DNS provider's module if the box hasn't got it. Outside the lock: building takes
    /// minutes, and the live proxy keeps serving meanwhile.
    /// </summary>
    private async Task<bool> EnsureDnsImageAsync(string host, DnsProvider dns, CancellationToken cancellationToken)
    {
        var colon = dns.Image.IndexOf(':', StringComparison.Ordinal);
        var built = await Capture(BuildImageExistsArguments(host, dns.Image[..colon], dns.Image[(colon + 1)..]), cancellationToken).ConfigureAwait(false);
        if (built.ExitCode == 0)
        {
            return true;
        }

        WriteHeading($"Building {dns.Image} on {host} (Caddy with {dns.Module})…");
        var context = Path.Combine(Path.GetTempPath(), $"rask-caddy-{Guid.NewGuid():N}");
        _fileSystem.CreateDirectory(context);
        try
        {
            _fileSystem.WriteAllText(Path.Combine(context, "Dockerfile"), dns.Dockerfile);
            if (await Run(BuildCaddyImageArguments(host, dns, context), cancellationToken).ConfigureAwait(false) == 0)
            {
                return true;
            }

            Console.WriteErrorLine($"Building {dns.Image} failed — its output is above. The proxy was left as it is.", ConsoleStyle.Error);
            return false;
        }
        finally
        {
            _fileSystem.TryDeleteDirectory(context);
        }
    }

    /// <summary>
    /// Give every other app with a domain its own file, routed to the container that serves it now: the one the old
    /// config names, else the one its own file names, else its only one — and the newest, said out loud, when nothing
    /// tells two apart.
    /// </summary>
    private async Task<bool> CarryOverOtherAppsAsync(
        string host, string slug, IReadOnlyList<DeployedApp> apps, string oldBase, DnsProvider? dns, CancellationToken cancellationToken)
    {
        var routed = RoutedContainers(oldBase);
        var carried = 0;
        foreach (var containers in ProxiedApps(apps, slug))
        {
            var app = containers[0].App;
            var ownFile = containers.Count > 1
                ? await ReadProxyFileAsync(host, CaddySites.FragmentPath(app), cancellationToken).ConfigureAwait(false)
                : null;
            var choice = ChooseServing(containers, routed, RoutedContainers(ownFile ?? string.Empty));
            var serving = choice.Container;
            if (choice.Guessed)
            {
                Console.WriteErrorLine(
                    $"  ! '{app}' has {containers.Count} containers ({string.Join(", ", containers.Select(c => c.Container))}) and the proxy named none of them — routing it to the newest, {serving.Container}.",
                    ConsoleStyle.Warning);
            }

            var fragment = BuildFragment(serving.Domain, new RouteTarget(serving.Container, serving.Port, serving.Redirects), dns);
            if (!await WriteProxyFileAsync(host, app, CaddySites.FragmentPath(app), fragment, cancellationToken).ConfigureAwait(false))
            {
                return false;
            }

            carried++;
        }

        Console.WriteLine($"  The proxy now keeps one config file per app in {CaddySites.SitesDirectory}; {carried} other app(s) carried over as they run now.", ConsoleStyle.Dim);
        return true;
    }

    /// <summary>
    /// Drop the file of an app none of whose containers is running any more (removed by hand, or moved to a bare port):
    /// its routes could only fail, and a name it held would block the app that takes it next.
    /// </summary>
    private async Task DropDeadAppsAsync(string host, string slug, IReadOnlyList<DeployedApp> apps, CancellationToken cancellationToken)
    {
        var listing = await Capture(BuildSitesListArguments(host), cancellationToken).ConfigureAwait(false);
        if (listing.ExitCode != 0)
        {
            return;
        }

        foreach (var app in CaddySites.AppsWithFragments(listing.StandardOutput))
        {
            if (string.Equals(app, slug, StringComparison.Ordinal)
                || apps.Any(a => a.Domain.Length > 0 && string.Equals(a.App, app, StringComparison.Ordinal)))
            {
                continue;
            }

            if (await Run(BuildProxyRemoveFileArguments(host, CaddySites.FragmentPath(app)), cancellationToken).ConfigureAwait(false) == 0)
            {
                Console.WriteLine($"  Dropped the proxy's routes for '{app}' — none of its containers is running.", ConsoleStyle.Dim);
            }
        }
    }

    /// <summary>Put this app's routes back as they were and reload. <see cref="SwitchOutcome.Stuck"/> when even that failed.</summary>
    private async Task<SwitchOutcome> UndoAsync(ProxySwitch change, SwitchUndo undo, CancellationToken cancellationToken)
    {
        var (host, slug) = (change.Host, change.Slug);
        var path = CaddySites.FragmentPath(slug);
        var restored = undo.Fragment is { Length: > 0 } fragment
            ? await WriteProxyFileAsync(host, slug, path, fragment, cancellationToken).ConfigureAwait(false)
            : await Run(BuildProxyRemoveFileArguments(host, path), cancellationToken).ConfigureAwait(false) == 0;
        if (restored && undo.Base is { Length: > 0 } previousBase)
        {
            restored = await WriteProxyFileAsync(host, slug, CaddySites.BasePath, previousBase, cancellationToken).ConfigureAwait(false);
        }

        if (restored && await RunWithRetryAsync(BuildCaddyReloadArguments(host), cancellationToken).ConfigureAwait(false) == 0)
        {
            Console.WriteErrorLine($"Put {slug}'s routes back as they were — the previous version is still serving.", ConsoleStyle.Warning);
            return SwitchOutcome.NotRouted;
        }

        Console.WriteErrorLine(
            $"Putting {slug}'s routes back failed too. {change.Target.Container} is left running, since the proxy may still send traffic to it — check `docker -H ssh://{host} exec {CaddyContainer} cat {path}` and deploy again.",
            ConsoleStyle.Error);
        return SwitchOutcome.Stuck;
    }

    private async Task<IReadOnlyList<DeployedApp>?> ListAppsAsync(string host, CancellationToken cancellationToken)
    {
        var listed = await Capture(BuildListArguments(host), cancellationToken).ConfigureAwait(false);
        if (listed.ExitCode == 0)
        {
            return ParseDeployedApps(listed.StandardOutput);
        }

        Console.WriteErrorLine($"Listing the containers on {HostName(host)} failed: {listed.StandardError.Trim()}", ConsoleStyle.Error);
        return null;
    }

    /// <summary>A file in the proxy container, or <c>null</c> when it isn't there.</summary>
    private async Task<string?> ReadProxyFileAsync(string host, string path, CancellationToken cancellationToken)
    {
        var read = await Capture(BuildProxyReadArguments(host, path), cancellationToken).ConfigureAwait(false);
        return read.ExitCode == 0 ? read.StandardOutput : null;
    }

    /// <summary>
    /// Write a file in the proxy: copied in beside it, then moved over it, so Caddy never reads half of one. A failed
    /// step leaves the file as it was, and fails the deploy.
    /// </summary>
    private async Task<bool> WriteProxyFileAsync(string host, string slug, string path, string content, CancellationToken cancellationToken)
    {
        // Unguessable and owner-only: on a shared machine a predictable name in the temp dir is one another user can
        // create first, and whatever it held would be copied into the proxy that fronts every app on the box.
        var local = Path.Combine(Path.GetTempPath(), $"rask-{slug}-{Guid.NewGuid():N}{CaddySites.FragmentExtension}");
        _fileSystem.WriteSecretText(local, content);
        var staged = CaddySites.StagingPath(path);
        int copied;
        try
        {
            copied = await Run(BuildProxyCopyArguments(host, local, staged), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _fileSystem.TryDelete(local);
        }

        if (copied == 0 && await Run(BuildProxyMoveArguments(host, staged, path), cancellationToken).ConfigureAwait(false) == 0)
        {
            return true;
        }

        await Run(BuildProxyRemoveFileArguments(host, staged), cancellationToken).ConfigureAwait(false); // ignore-absent
        Console.WriteErrorLine($"Writing {path} in the proxy failed — its output is above. It was left as it was.", ConsoleStyle.Error);
        return false;
    }

    private async Task<bool> ProxyExistsAsync(string host, CancellationToken cancellationToken) =>
        (await Capture(BuildCaddyDnsLabelArguments(host), cancellationToken).ConfigureAwait(false)).ExitCode == 0;

    private async Task<bool> NetworkExistsAsync(string host, CancellationToken cancellationToken) =>
        (await Capture(BuildNetworkExistsArguments(host, Network), cancellationToken).ConfigureAwait(false)).ExitCode == 0;

    private DeployLock CreateLock() =>
        new(Console, _process, _workingDirectory) { PollDelay = LockPollDelay, Attempts = LockAttempts };
}
