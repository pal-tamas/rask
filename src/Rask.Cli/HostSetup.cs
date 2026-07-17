using System.ComponentModel;
using System.Globalization;

namespace Rask.Cli;

/// <summary>How far <c>rask deploy</c> is allowed to go in changing the host.</summary>
internal enum SetupMode
{
    /// <summary>Ask first on a terminal; refuse to touch the host when there's nobody to ask.</summary>
    Ask,

    /// <summary>Do it without asking (<c>--setup-host</c>) — the CI/scripted path.</summary>
    Forced,

    /// <summary>Never touch the host (<c>--no-setup-host</c>); fail with guidance instead.</summary>
    Disabled,
}

/// <summary>
/// Turns a bare box into one <c>rask deploy</c> can use, without the user ever opening an SSH session.
///
/// <para>The whole class exists to honour one rule: <strong>never revoke the access you're currently
/// using until a fresh connection proves the replacement works.</strong> Preparation (Docker, the
/// deploy login) can't cost you the box, so it runs first and unguarded. Only once a brand-new
/// connection as the new login has succeeded do the risky steps (firewall, SSH hardening) run — and
/// then only behind a rollback timer armed on the host itself, so a lockout heals even if this process
/// is killed.</para>
/// </summary>
internal sealed class HostSetup(IConsole console, IProcessRunner process)
{
    /// <summary>Proves the login works AND that it can drive Docker — the two things a deploy needs.</summary>
    internal const string VerifyScript = "docker info >/dev/null 2>&1 && echo rask-ok";

    /// <summary>
    /// Proves only that we can still log in. Used <em>after</em> the firewall and hardening, where the
    /// question is "are we locked out?" and nothing else: Docker was already proved before those ran,
    /// so folding it in again would let a transient daemon hiccup masquerade as a lockout and trigger a
    /// five-minute rollback of changes that were fine.
    /// </summary>
    internal const string ReachableScript = "echo rask-ok";

    private const string VerifyToken = "rask-ok";

    /// <summary>Delay between verification attempts, and how many — zeroed in tests, as in <see cref="Commands.DeployCommand"/>.</summary>
    internal TimeSpan ReadinessDelay { get; set; } = TimeSpan.FromSeconds(2);

    internal int ReadinessAttempts { get; set; } = 5;

    /// <summary>
    /// Make <paramref name="target"/> deployable. Returns the target to deploy as — which may carry a
    /// <em>different login</em> than the one passed in, once a deploy user has replaced root — or
    /// <c>null</c> if we couldn't or shouldn't proceed (the reason is already on the console).
    /// </summary>
    public async Task<SshTarget?> EnsureReadyAsync(SshTarget target, BootstrapOptions options, SetupMode mode, CancellationToken cancellationToken)
    {
        var facts = await HostProbe.ProbeAsync(process, console, target, cancellationToken).ConfigureAwait(false);
        if (facts is null)
        {
            return null; // unreachable — ProbeAsync already said why, in ssh's own words
        }

        // A box that already runs Docker is deployable, and deploying is the job. Setup exists to make
        // a box deployable — not to re-offer improvements to one that's already serving. Without this,
        // a perfectly good host (a least-privilege deploy login, no sudo, a cloud firewall instead of
        // ufw) would be nagged on every deploy and then *fail* on the sudo check below. `--setup-host`
        // is the explicit "prepare this host anyway", so it still gets the remaining work.
        if (facts.DockerReady && mode != SetupMode.Forced)
        {
            return target;
        }

        // Resolved locally (no connection) and unioned into the firewall's allow list — see
        // BootstrapOptions.ConnectPort for why sshd -T alone isn't enough.
        var connectPort = await ResolveConnectPortAsync(target, cancellationToken).ConfigureAwait(false);
        var plan = HostBootstrap.Plan(facts, options with { ConnectPort = connectPort });

        if (plan.IsEmpty)
        {
            // Nothing to do. Either the box is ready, or it's broken in a way we don't fix.
            // Warnings are deliberately silent here: nothing is being changed, so there's nothing to
            // disclose about this run, and repeating them on every redeploy would just be noise.
            if (facts.DockerReady)
            {
                return target;
            }

            console.WriteErrorLine($"Can't deploy to '{target}': {facts.DockerDiagnosis}.", ConsoleStyle.Error);
            console.Error.WriteLine(!facts.CanSudo
                ? $"Rask needs root or passwordless sudo on the host to fix that — deploy as root (e.g. --host root@{target.Host}) for the first deploy."
                : "This box has no systemd, so Rask can't manage the Docker daemon on it. Start Docker yourself and re-run.");
            return null;
        }

        // Checked before we ask: there's no point confirming a plan we can't carry out.
        if (!facts.CanSudo)
        {
            // ...but if the box already deploys, not being allowed to improve it is no reason to fail
            // the deploy. Say what we skipped and carry on.
            if (facts.DockerReady)
            {
                console.WriteErrorLine($"  ! Skipped host setup — '{facts.User}' is neither root nor able to run sudo without a password. Deploying anyway.", ConsoleStyle.Warning);
                return target;
            }

            console.WriteErrorLine($"Can't set up '{target}': '{facts.User}' is neither root nor able to run sudo without a password.", ConsoleStyle.Error);
            console.Error.WriteLine($"Deploy as root (e.g. --host root@{target.Host}) for the first deploy, or give the user passwordless sudo.");
            return null;
        }

        if (!await ConfirmAsync(target, plan, mode).ConfigureAwait(false))
        {
            return null;
        }

        // ── Preparation: nothing here can cost us access to the box. ─────────────────────────────────
        foreach (var step in plan.Preparation)
        {
            if (!await RunStepAsync(target, step, cancellationToken).ConfigureAwait(false))
            {
                return null;
            }
        }

        // The login we'll use from here on. Every bootstrap step still runs over the ORIGINAL target,
        // because that's the privilege level the scripts were built for.
        var deployTarget = plan.NewUser is null ? target : target.WithUser(plan.NewUser);

        // ── The gate. Prove the new login works before anything can take the old one away. ───────────
        if (plan.NewUser is not null && !await VerifyAsync(deployTarget, VerifyScript, cancellationToken).ConfigureAwait(false))
        {
            console.WriteErrorLine($"Couldn't connect as '{deployTarget}' after creating it — stopping here.", ConsoleStyle.Error);
            console.Error.WriteLine("Nothing that could lock you out has been changed: the firewall and SSH config are untouched.");
            return null;
        }

        if (plan.Risky.Count == 0)
        {
            return deployTarget;
        }

        // ── Risky: these CAN cost us access, so they run behind the rollback guard. ──────────────────
        // Disarming has to happen over the new login: SSH hardening may have just killed the old one,
        // and every ssh invocation is a fresh connection.
        var disarmAsRoot = plan.NewUser is null && facts.IsRoot;

        if (!await RunScriptAsync(target, HostBootstrap.ArmGuardScript(plan, facts.IsRoot), cancellationToken).ConfigureAwait(false))
        {
            console.WriteErrorLine("Couldn't arm the rollback guard on the host — refusing to touch the firewall or SSH config without it.", ConsoleStyle.Error);
            return null;
        }

        foreach (var step in plan.Risky)
        {
            if (await RunStepAsync(target, step, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            await AbandonAsync(deployTarget, disarmAsRoot, $"Host setup failed at: {step.Description}", cancellationToken).ConfigureAwait(false);
            return null;
        }

        // ── The second gate: are we still in, after the changes that could shut us out? ──────────────
        // Reachability only — Docker was already proved above, and folding it back in here would let a
        // daemon hiccup look like a lockout and roll back changes that were fine.
        if (!await VerifyAsync(deployTarget, HostSetup.ReachableScript, cancellationToken).ConfigureAwait(false))
        {
            console.WriteErrorLine($"Locked out: can't reach '{deployTarget}' after hardening the host.", ConsoleStyle.Error);
            console.Error.WriteLine($"Leave it alone — the guard on the box reverts the firewall and SSH changes in about {HostBootstrap.GuardDelay}. Then try again.");
            return null;
        }

        // The disarm is not optional bookkeeping: if it doesn't land, the guard fires in
        // GuardDelay and quietly undoes the firewall and hardening we're about to call done. Retried,
        // then reported as a failure — telling the user the host is ready would be false.
        if (!await DisarmAsync(deployTarget, disarmAsRoot, cancellationToken).ConfigureAwait(false))
        {
            console.WriteErrorLine($"Set the host up, but couldn't switch off its rollback guard — it will undo the firewall and SSH hardening in about {HostBootstrap.GuardDelay}.", ConsoleStyle.Error);
            console.Error.WriteLine($"Nothing is broken and the box stays reachable. Re-run `rask deploy --setup-host` once it has reverted.");
            return null;
        }

        console.WriteLine($"Host ready: {deployTarget}", ConsoleStyle.Success);
        return deployTarget;
    }

    /// <summary>Disarm the guard, retried — a single dropped ssh call must not cost us the setup.</summary>
    private async Task<bool> DisarmAsync(SshTarget target, bool asRoot, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            if (await RunScriptAsync(target, HostBootstrap.DisarmGuardScript(asRoot), cancellationToken).ConfigureAwait(false))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// A step failed after the guard was armed. If we can still get in there's no lockout to heal, so
    /// disarm and keep what did succeed; if we can't, say plainly that the box repairs itself.
    /// </summary>
    private async Task AbandonAsync(SshTarget deployTarget, bool disarmAsRoot, string reason, CancellationToken cancellationToken)
    {
        console.WriteErrorLine(reason, ConsoleStyle.Error);

        if (await VerifyAsync(deployTarget, ReachableScript, cancellationToken).ConfigureAwait(false)
            && await DisarmAsync(deployTarget, disarmAsRoot, cancellationToken).ConfigureAwait(false))
        {
            console.Error.WriteLine("The host is still reachable and nothing was rolled back.");
        }
        else
        {
            console.Error.WriteLine($"The host is no longer reachable — the guard reverts the firewall and SSH changes in about {HostBootstrap.GuardDelay}.");
        }
    }

    /// <summary>
    /// Open a <em>brand-new</em> connection and confirm the login can drive Docker. Retried, because a
    /// freshly-installed daemon or a just-granted group can take a moment to settle.
    /// </summary>
    private async Task<bool> VerifyAsync(SshTarget target, string script, CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < ReadinessAttempts; attempt++)
        {
            if (attempt > 0 && ReadinessDelay > TimeSpan.Zero)
            {
                await Task.Delay(ReadinessDelay, cancellationToken).ConfigureAwait(false);
            }

            // freshConnection: a multiplexed session would reuse the already-open channel and prove nothing.
            var args = new List<string>(target.ConnectionArguments(freshConnection: true)) { script };
            var result = await CaptureAsync(args, cancellationToken).ConfigureAwait(false);
            if (result.ExitCode == 0 && result.StandardOutput.Contains(VerifyToken, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The SSH port this session actually uses, per <c>ssh -G</c> — which resolves <c>~/.ssh/config</c>,
    /// so it knows the port behind a bare alias. Local only: no connection is made. This is unioned with
    /// whatever <c>sshd -T</c> reports, because the two disagree when sshd is socket-activated and
    /// closing the port we're talking on is the one mistake we can't recover from.
    /// </summary>
    private async Task<int?> ResolveConnectPortAsync(SshTarget target, CancellationToken cancellationToken)
    {
        if (target.Port is { } explicitPort)
        {
            return explicitPort;
        }

        var result = await CaptureAsync(["-G", "--", target.Destination], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
        {
            return null;
        }

        foreach (var raw in result.StandardOutput.Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("port ", StringComparison.Ordinal)
                && int.TryParse(line[5..].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
                && port is > 0 and <= 65535)
            {
                return port;
            }
        }

        return null;
    }

    private async Task<bool> RunStepAsync(SshTarget target, BootstrapStep step, CancellationToken cancellationToken)
    {
        ProcessResult result;

        // Scoped so the spinner's line is cleared before anything else is written — otherwise a failing
        // step's stderr interleaves with the animation.
        await using (Spinner.Start(console, step.Description + "…"))
        {
            var args = new List<string>(target.ConnectionArguments()) { step.Script };
            result = await CaptureAsync(args, cancellationToken).ConfigureAwait(false);
        }

        if (result.ExitCode == 0)
        {
            console.WriteLine($"  + {step.Description}", ConsoleStyle.Success);
            return true;
        }

        WriteRemoteError(result);
        return false;
    }

    private async Task<bool> RunScriptAsync(SshTarget target, string script, CancellationToken cancellationToken)
    {
        var args = new List<string>(target.ConnectionArguments()) { script };
        var result = await CaptureAsync(args, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return true;
        }

        WriteRemoteError(result);
        return false;
    }

    /// <summary>The remote script explained itself (a failed apt, a rejected sshd config) — pass it through.</summary>
    private void WriteRemoteError(ProcessResult result)
    {
        var detail = result.StandardError.Trim();
        if (detail.Length > 0)
        {
            console.Error.WriteLine(detail);
        }
    }

    private async Task<ProcessResult> CaptureAsync(IReadOnlyList<string> args, CancellationToken cancellationToken)
    {
        try
        {
            return await process.CaptureAsync("ssh", args, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Win32Exception)
        {
            return new ProcessResult(-1, string.Empty, "`ssh` isn't installed or isn't on your PATH.");
        }
    }

    /// <summary>Show the plan and get a yes — or explain how to give one non-interactively.</summary>
    private Task<bool> ConfirmAsync(SshTarget target, BootstrapPlan plan, SetupMode mode)
    {
        if (mode == SetupMode.Forced)
        {
            // Nothing else prints them on this path, and what we refused to do still has to be said.
            WriteWarnings(plan);
            return Task.FromResult(true);
        }

        console.WriteLine($"'{target}' isn't ready to deploy to. Rask can set it up:", ConsoleStyle.Heading);
        console.Out.WriteLine();
        foreach (var step in plan.AllSteps)
        {
            console.Out.WriteLine($"  • {step.Description}");
        }

        // With the plan, not after the prompt — they qualify the list the user is about to approve.
        WriteWarnings(plan);
        console.Out.WriteLine();

        if (mode == SetupMode.Disabled)
        {
            console.WriteErrorLine("Host setup is disabled (--no-setup-host), so there's nothing more to do.", ConsoleStyle.Error);
            return Task.FromResult(false);
        }

        var prompt = new Prompt(console);
        if (!prompt.Interactive)
        {
            // Nobody to ask. Changing a production host on an assumption is not a default we'll take.
            console.WriteErrorLine("Not running on a terminal, so Rask won't change the host without being told to.", ConsoleStyle.Error);
            console.Error.WriteLine("Re-run from a terminal to confirm, or pass --setup-host to skip the prompt.");
            return Task.FromResult(false);
        }

        if (prompt.Confirm($"Set up {target} now?", @default: true))
        {
            return Task.FromResult(true);
        }

        console.Error.WriteLine("Left the host alone.");
        return Task.FromResult(false);
    }

    private void WriteWarnings(BootstrapPlan plan)
    {
        // Anything we refused to do is said out loud — a silently-skipped firewall reads as a firewall.
        foreach (var warning in plan.Warnings)
        {
            console.WriteErrorLine($"  ! {warning}", ConsoleStyle.Warning);
        }
    }
}
