using System.Globalization;

namespace Rask.Cli;

/// <summary>What the user asked <c>rask deploy</c> to do to the host (after flags and defaults are merged).</summary>
/// <param name="DeployUser">The non-root login to create and switch to, or <c>null</c> to keep the current one.</param>
/// <param name="PublishedPort">The <c>--port</c> to open, or <c>null</c> in domain mode (which opens 80/443 for Caddy).</param>
/// <param name="ConnectPort">
/// The SSH port this session is actually connected on, resolved locally by <c>ssh -G</c>. Always
/// allowed through the firewall on top of whatever <c>sshd -T</c> reports — those differ when sshd is
/// socket-activated, and the port we're using is the one we cannot afford to close.
/// </param>
internal sealed record BootstrapOptions(string? DeployUser, bool Firewall, bool HardenSsh, int? PublishedPort, int? ConnectPort = null)
{
    /// <summary>The default non-root login <c>rask deploy</c> creates on a box it's handed as root.</summary>
    public const string DefaultDeployUser = "deploy";
}

/// <summary>One idempotent remote step: what we'd tell the user we're doing, and the shell that does it.</summary>
/// <param name="Undo">
/// For a risky step, the shell that puts this box back as it was — and <em>only</em> what this step
/// changed. The rollback guard is built by joining these, so it can never revert state this run didn't
/// create (a firewall the user already ran, or a previous deploy's sshd drop-in).
/// </param>
internal sealed record BootstrapStep(string Description, string Script, string? Undo = null);

/// <summary>
/// An ordered plan for turning a host into one <c>rask deploy</c> can use.
///
/// <para>The split between <see cref="Preparation"/> and <see cref="Risky"/> is the safety contract:
/// preparation can't cost you access to the box, <see cref="Risky"/> can. Everything in
/// <see cref="Risky"/> runs only after a fresh connection has proved the <see cref="NewUser"/> login
/// works, and only behind the rollback guard.</para>
/// </summary>
/// <param name="NewUser">The login to deploy as once the plan has run, or <c>null</c> to keep the current one.</param>
/// <param name="Warnings">Things we deliberately refused to do, and why — never silently dropped.</param>
internal sealed record BootstrapPlan(
    IReadOnlyList<BootstrapStep> Preparation,
    IReadOnlyList<BootstrapStep> Risky,
    string? NewUser,
    IReadOnlyList<string> Warnings)
{
    public bool IsEmpty => Preparation.Count == 0 && Risky.Count == 0;

    public IEnumerable<BootstrapStep> AllSteps => Preparation.Concat(Risky);
}

/// <summary>
/// Turns <see cref="HostFacts"/> into the shell that fixes the host. Everything here is a pure
/// function of the facts and the options — no I/O — so the entire risky surface (installing Docker,
/// creating a login, enabling a firewall, disabling root SSH) is unit-testable by exact string
/// comparison, against every host shape, without a box to break.
///
/// <para>This is the deliberate reversal of the old "we never auto-install Docker" stance
/// (<see cref="DockerProbe"/>): it holds for the <em>local</em> CLI, but the remote box is precisely
/// the thing the user is paying <c>rask deploy</c> to manage.</para>
/// </summary>
internal static class HostBootstrap
{
    /// <summary>The transient systemd unit that undoes the risky steps if we never confirm we're still in.</summary>
    internal const string GuardUnit = "rask-rollback";

    /// <summary>The sentinel whose presence tells the guard the client got back in. On tmpfs — a reboot clears it.</summary>
    internal const string GuardSentinel = "/run/rask-setup-ok";

    /// <summary>Our sshd drop-in. A separate file so the guard can undo hardening with a single <c>rm</c>.</summary>
    internal const string SshDropIn = "/etc/ssh/sshd_config.d/99-rask.conf";

    /// <summary>How long the guard waits before assuming we're locked out and reverting.</summary>
    internal const string GuardDelay = "5min";

    /// <summary>
    /// Build the plan. Order within <see cref="BootstrapPlan.Preparation"/> and
    /// <see cref="BootstrapPlan.Risky"/> is the execution order and is load-bearing.
    /// </summary>
    public static BootstrapPlan Plan(HostFacts facts, BootstrapOptions options)
    {
        var preparation = new List<BootstrapStep>();
        var risky = new List<BootstrapStep>();
        var warnings = new List<string>();

        // A deploy user only makes sense when we were handed root (the fresh-VPS case). An existing
        // non-root login already is what the deploy user would be; creating a second one is noise.
        var newUser = options.DeployUser is { } wanted && facts.IsRoot && !string.Equals(facts.User, wanted, StringComparison.Ordinal)
            ? wanted
            : null;

        if (newUser is not null && !IsValidUserName(newUser))
        {
            throw new ArgumentException($"'{newUser}' isn't a valid Linux user name.", nameof(options));
        }

        // The login we'll deploy as once this plan has run — decides whether disabling root SSH is safe.
        var finalUser = newUser ?? facts.User;
        var finalIsRoot = newUser is null && facts.IsRoot;

        if (!facts.DockerInstalled)
        {
            preparation.Add(new BootstrapStep("Install Docker", Privileged(InstallDockerScript, facts.IsRoot)));
        }

        // A fresh get.docker.com install already enables the daemon, but an install we didn't do may
        // have left it masked or stopped. Cheap and idempotent either way.
        if (!facts.DockerUsable && facts.HasSystemd)
        {
            preparation.Add(new BootstrapStep("Start the Docker daemon", Privileged("set -e\nsystemctl enable --now docker", facts.IsRoot)));
        }

        if (newUser is not null)
        {
            preparation.Add(new BootstrapStep(
                $"Create the '{newUser}' login and give it Docker access",
                Privileged(CreateDeployUserScript(newUser, facts.User), facts.IsRoot)));
        }
        else if (!facts.IsRoot && !facts.InDockerGroup)
        {
            preparation.Add(new BootstrapStep(
                $"Add '{facts.User}' to the docker group",
                Privileged($"set -e\nusermod -aG docker {facts.User}", facts.IsRoot)));
        }

        if (options.Firewall)
        {
            AddFirewall(facts, options, risky, warnings);
        }

        if (options.HardenSsh)
        {
            AddHardening(facts, finalUser, finalIsRoot, risky, warnings);
        }

        return new BootstrapPlan(preparation, risky, newUser, warnings);
    }

    // ── Firewall ────────────────────────────────────────────────────────────────────────────────────

    private static void AddFirewall(HostFacts facts, BootstrapOptions options, List<BootstrapStep> risky, List<string> warnings)
    {
        // Already firewalled — the box's own rules are the user's business. Checked first so a ready
        // host stays a silent no-op rather than warning about ports we no longer need to know.
        if (facts.UfwActive)
        {
            return;
        }

        // ufw is Debian/Ubuntu's firewall. On a box with neither it nor apt-get to install it from
        // (Fedora, RHEL, Alpine — all of which run Docker fine), there is no firewall step to plan.
        // Deciding that here, rather than `exit 1` from the script, is the difference between "we
        // skipped the firewall" and aborting a deploy on a box that is otherwise perfectly ready.
        if (!facts.UfwInstalled && !facts.HasApt)
        {
            warnings.Add("Firewall skipped — ufw isn't installed and this box has no apt-get to install it from. Configure your own firewall (or your provider's) for this host.");
            return;
        }

        // The lockout guard. We open the ports sshd actually listens on, read off the box — never a
        // guessed 22, and never parsed from --host (an ssh-config alias hides the real port). If we
        // couldn't read them, we don't know what to keep open, so we don't touch the firewall at all.
        if (facts.SshPorts.Count == 0 && options.ConnectPort is null)
        {
            warnings.Add("Firewall skipped — couldn't read sshd's listening ports from the host, so enabling ufw could lock you out. Rask won't guess port 22.");
            return;
        }

        var ports = new List<int>(facts.SshPorts);

        // The port we're actually connected on, always. `sshd -T` reports sshd_config's Port, which is
        // NOT the listening port when sshd is socket-activated (systemd's .socket owns it there) — so
        // trusting it alone would firewall off the very port this session is using.
        if (options.ConnectPort is { } connect && !ports.Contains(connect))
        {
            ports.Add(connect);
        }

        ports.Sort();
        ports.AddRange(options.PublishedPort is { } port ? [port] : [80, 443]);

        var script = new List<string> { "set -e" };
        if (!facts.UfwInstalled)
        {
            script.Add(EnsureUfwInstalled);
        }

        // Allow BEFORE enable, always — the ordering that keeps you in the box.
        foreach (var p in ports)
        {
            script.Add($"ufw allow {p.ToString(CultureInfo.InvariantCulture)}/tcp");
        }

        script.Add("ufw default deny incoming");
        script.Add("ufw default allow outgoing");
        script.Add("ufw --force enable");

        var opened = string.Join(", ", ports.Select(p => p.ToString(CultureInfo.InvariantCulture)));
        risky.Add(new BootstrapStep(
            $"Enable the firewall (allow {opened}; deny everything else inbound)",
            Privileged(string.Join('\n', script), facts.IsRoot),
            // ufw was inactive before this step, so disabling it restores exactly the prior state.
            Undo: "ufw --force disable;"));
    }

    // ── SSH hardening ───────────────────────────────────────────────────────────────────────────────

    private static void AddHardening(HostFacts facts, string finalUser, bool finalIsRoot, List<BootstrapStep> risky, List<string> warnings)
    {
        // We refuse to harden a config we couldn't read: without sshd -T we can't tell what's already
        // in effect, and we'd have no way to prove afterwards that the change actually applied.
        if (!facts.SshdReadable)
        {
            warnings.Add("SSH hardening skipped — couldn't read sshd's effective config from the host, so Rask can't verify a change would apply.");
            return;
        }

        // Our drop-in is only read if sshd_config includes the directory. On a box without that line
        // we'd write a file that changes nothing and report success — a lie. Skip and say so.
        if (!facts.SshConfigInclude)
        {
            warnings.Add($"SSH hardening skipped — /etc/ssh/sshd_config has no `Include /etc/ssh/sshd_config.d/*.conf` line, so {SshDropIn} would be ignored.");
            return;
        }

        // The file always carries the FULL desired set, never just the settings currently missing:
        // it's our own file, so rewriting it with a subset would revert whatever the rest of it had
        // already established.
        var desired = new List<string> { "PasswordAuthentication no", "KbdInteractiveAuthentication no" };
        var described = "disable SSH password login";

        // Only pull up the ladder once we're standing on the other one. If root is still the login we
        // deploy as (no deploy user was created), disabling root SSH locks us out on the next deploy.
        if (!finalIsRoot)
        {
            desired.Add("PermitRootLogin no");
            described = "disable SSH password login and root login";
        }
        else if (facts.SshRootLoginPermitted)
        {
            warnings.Add($"Root SSH login left enabled — you're deploying as '{finalUser}'. Use --deploy-user to create a non-root login and disable it.");
        }

        // Whether to run at all is a separate question from what to write: skip only when every desired
        // setting is already in effect, so a healthy host stays a no-op and never re-prompts.
        var needed = facts.SshPasswordAuthEnabled || facts.SshKbdAuthEnabled || (!finalIsRoot && facts.SshRootLoginPermitted);
        if (!needed)
        {
            return;
        }

        risky.Add(new BootstrapStep(
            $"Harden SSH ({described})",
            Privileged(HardenScript(desired), facts.IsRoot),
            // Our drop-in is the only thing this step wrote; removing it restores the box's own config.
            Undo: $"rm -f {SshDropIn}; systemctl reload ssh 2>/dev/null || systemctl reload sshd 2>/dev/null || true;"));
    }

    // ── The rollback guard ──────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Arm a transient systemd timer that undoes the risky steps unless <see cref="GuardSentinel"/>
    /// appears. It keys on a sentinel file rather than a PID so it still fires if the CLI is killed
    /// mid-run — the failure mode we're protecting against is precisely "the client can't get back in".
    /// </summary>
    /// <param name="plan">
    /// The guard reverts exactly the risky steps in <em>this</em> plan, via their
    /// <see cref="BootstrapStep.Undo"/> — never a fixed list. A plan that only hardens SSH must not
    /// disable a firewall the user was already running, and a plan that only touches the firewall must
    /// not delete an sshd drop-in a previous deploy put there.
    /// </param>
    public static string ArmGuardScript(BootstrapPlan plan, bool isRoot)
    {
        var undo = string.Join(' ', plan.Risky.Select(s => s.Undo).Where(u => !string.IsNullOrEmpty(u)));
        return Privileged($$"""
            rm -f {{GuardSentinel}}
            systemctl stop {{GuardUnit}}.timer 2>/dev/null || true
            systemctl reset-failed {{GuardUnit}}.service 2>/dev/null || true
            systemd-run --unit={{GuardUnit}} --on-active={{GuardDelay}} /bin/sh -c '[ -f {{GuardSentinel}} ] || { {{undo}} }'
            """, isRoot);
    }

    /// <summary>Disarm — only ever called after a brand-new connection proved we're still in.</summary>
    public static string DisarmGuardScript(bool isRoot) => Privileged($$"""
        touch {{GuardSentinel}}
        systemctl stop {{GuardUnit}}.timer 2>/dev/null || true
        """, isRoot);

    // ── Scripts ─────────────────────────────────────────────────────────────────────────────────────

    // get.docker.com is Docker's own convenience script and the standard one-liner for a single box.
    // Downloaded first, then run: a truncated `curl | sh` can execute half a script.
    private const string InstallDockerScript = """
        set -e
        if ! command -v curl >/dev/null 2>&1; then
          if command -v apt-get >/dev/null 2>&1; then apt-get update -qq; DEBIAN_FRONTEND=noninteractive apt-get install -y -qq curl;
          elif command -v dnf >/dev/null 2>&1; then dnf install -y -q curl;
          elif command -v yum >/dev/null 2>&1; then yum install -y -q curl;
          else echo "rask: need curl to install Docker, and no known package manager to install it with" >&2; exit 1; fi
        fi
        curl -fsSL https://get.docker.com -o /tmp/rask-get-docker.sh
        sh /tmp/rask-get-docker.sh
        rm -f /tmp/rask-get-docker.sh
        """;

    private const string EnsureUfwInstalled = """
        if ! command -v ufw >/dev/null 2>&1; then
          if command -v apt-get >/dev/null 2>&1; then apt-get update -qq; DEBIAN_FRONTEND=noninteractive apt-get install -y -qq ufw;
          else echo "rask: ufw isn't installed and this box has no apt-get to install it from" >&2; exit 1; fi
        fi
        """;

    /// <summary>
    /// Create the login, give it the deploying user's keys, Docker access, and passwordless sudo.
    ///
    /// <para>The sudoers file is written to a <c>.tmp</c> name first (sudo ignores files with a dot in
    /// them) and validated with <c>visudo -c</c> before being moved into place — a malformed file in
    /// <c>/etc/sudoers.d</c> can break sudo entirely.</para>
    /// <para>NOPASSWD sudo is not the privilege escalation it looks like: docker group membership is
    /// already root-equivalent. It's what makes re-provisioning idempotent.</para>
    /// </summary>
    private static string CreateDeployUserScript(string user, string fromUser) => $$"""
        set -e
        id -u {{user}} >/dev/null 2>&1 || useradd -m -s /bin/bash {{user}}
        SRC="$(getent passwd {{fromUser}} | cut -d: -f6)/.ssh/authorized_keys"
        DEST_HOME="$(getent passwd {{user}} | cut -d: -f6)"
        [ -f "$SRC" ] || { echo "rask: no authorized_keys at $SRC to copy — '{{user}}' would have no way to log in" >&2; exit 1; }
        install -d -m 700 "$DEST_HOME/.ssh"
        install -m 600 "$SRC" "$DEST_HOME/.ssh/authorized_keys"
        chown -R {{user}}: "$DEST_HOME/.ssh"
        groupadd -f docker
        usermod -aG docker {{user}}
        printf '{{user}} ALL=(ALL) NOPASSWD:ALL\n' > /etc/sudoers.d/rask-deploy.tmp
        chmod 440 /etc/sudoers.d/rask-deploy.tmp
        visudo -c -f /etc/sudoers.d/rask-deploy.tmp >/dev/null || { rm -f /etc/sudoers.d/rask-deploy.tmp; echo "rask: refusing to install a malformed sudoers file" >&2; exit 1; }
        mv /etc/sudoers.d/rask-deploy.tmp /etc/sudoers.d/rask-deploy
        """;

    /// <summary>
    /// Write the drop-in, validate the whole config with <c>sshd -t</c> <em>before</em> reloading, then
    /// read the effective config back with <c>sshd -T</c> to prove it applied. Reporting success on a
    /// no-op would be worse than not hardening at all.
    /// </summary>
    private static string HardenScript(IReadOnlyList<string> settings)
    {
        var lines = string.Concat(settings.Select(s => s + "\\n"));
        var checks = string.Join('\n', settings.Select(s =>
            $"sshd -T | grep -qix '{s}' || {{ echo \"rask: sshd didn't apply '{s}'\" >&2; exit 1; }}"));

        return $$"""
            set -e
            mkdir -p /etc/ssh/sshd_config.d
            printf '{{lines}}' > {{SshDropIn}}
            chmod 644 {{SshDropIn}}
            sshd -t || { rm -f {{SshDropIn}}; echo "rask: refusing to reload sshd with a config it rejects" >&2; exit 1; }
            systemctl reload ssh 2>/dev/null || systemctl reload sshd
            {{checks}}
            """;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Run a script with the privilege it needs. Non-root goes through <c>sudo -n sh -c</c> with the
    /// whole script quoted as one argument, so multi-line <c>if</c> blocks keep working.
    /// </summary>
    internal static string Privileged(string script, bool isRoot) =>
        isRoot ? script : $"sudo -n sh -c {ShellQuote(script)}";

    /// <summary>
    /// Wrap a string as a single-quoted POSIX shell word. Inside single quotes every character is
    /// literal, so the only escape needed is for the quote itself: end the quote, emit an escaped one,
    /// reopen. Nothing interpolated into a script can break out of it.
    /// </summary>
    internal static string ShellQuote(string value) =>
        "'" + value.Replace("'", "'\\''", StringComparison.Ordinal) + "'";

    /// <summary>
    /// A Linux user name we're willing to interpolate into a remote shell: the portable
    /// <c>useradd</c> set (<c>^[a-z_][a-z0-9_-]{0,31}$</c>). Anything else is rejected before we
    /// connect, not escaped and hoped for.
    /// </summary>
    public static bool IsValidUserName(string value) =>
        value.Length is > 0 and <= 32
        && (char.IsAsciiLetterLower(value[0]) || value[0] == '_')
        && value.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c is '_' or '-');
}
