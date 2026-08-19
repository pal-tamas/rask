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
/// <param name="ContainerPort">
/// The port <em>inside</em> the app container that <see cref="PublishedPort"/> maps to. Needed on top
/// of the published port because Docker's DNAT happens in <c>nat/PREROUTING</c>, <em>before</em> the
/// filter rules run: by the time a packet reaches <c>DOCKER-USER</c> its destination port is already
/// the container's, so that — not the host's — is the number the firewall has to allow.
/// </param>
internal sealed record BootstrapOptions(string? DeployUser, bool Firewall, bool HardenSsh, int? PublishedPort, int? ConnectPort = null, int? ContainerPort = null)
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
    /// ufw's own tail rules file. Our container rules go here rather than into a live <c>iptables</c>
    /// call because ufw reloads this file at boot — a raw chain would silently vanish on the first
    /// reboot and quietly hand back the exposure.
    /// </summary>
    internal const string AfterRules = "/etc/ufw/after.rules";

    /// <summary>The chain holding the container default-deny. Ours alone, so it's rewritten wholesale.</summary>
    internal const string DockerChain = "RASK-DOCKER";

    /// <summary>
    /// Fences for the block we own inside <see cref="AfterRules"/>, so re-running rewrites our rules
    /// and never the user's. Deliberately free of spaces and quotes: they're spliced into an unquoted
    /// <c>sed</c> address inside the guard's own <c>sh -c '…'</c>.
    /// </summary>
    internal const string BlockBegin = "###RASK-DOCKER-BEGIN";

    internal const string BlockEnd = "###RASK-DOCKER-END";

    /// <summary>
    /// Bumped whenever the rules inside the block change, so an existing block is recognised as stale
    /// even when it allows the same ports. It's the first half of the signature the probe reads back.
    /// </summary>
    internal const string RulesVersion = "v1";

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
        // Already firewalled — the box's own rules are the user's business, so we don't touch its allow
        // list. What we do still fix is that ufw isn't actually in the path of a published container
        // port (see AddDockerFirewall): a box with an active ufw is precisely the one whose owner
        // believes "deny incoming" already covers Docker.
        if (facts.UfwActive)
        {
            AddDockerFirewall(facts, options, risky, warnings);
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

        AddDockerFirewall(facts, options, risky, warnings);
    }

    /// <summary>
    /// Make the firewall apply to container ports too — the step that turns "deny everything else
    /// inbound" from a claim into a fact.
    ///
    /// <para>Docker publishes a port by writing its own iptables rules: a DNAT in
    /// <c>nat/PREROUTING</c> and an accept reached through <c>filter/FORWARD</c>. ufw's rules live on
    /// <c>INPUT</c>, which forwarded traffic never touches, so <c>ufw deny</c> has no bearing on
    /// anything <c>docker run -p</c> exposes. Enabling ufw next to Docker and saying nothing therefore
    /// buys a false sense of security: <c>ufw status</c> reports a port closed while the internet can
    /// reach it.</para>
    ///
    /// <para>The fix is Docker's own supported hook, <c>DOCKER-USER</c> — the chain it consults first
    /// and never writes to itself. We give it a default-deny for traffic arriving from off the box and
    /// RETURN only what this deploy actually publishes. Three properties matter and each is why a line
    /// is shaped the way it is:</para>
    /// <list type="bullet">
    /// <item>It lives in <c>/etc/ufw/after.rules</c>, not in a one-off <c>iptables</c> call, because
    /// raw chains do not survive a reboot — ufw reloads that file at boot, so the rules come back.</item>
    /// <item>It denies on the <em>outbound</em> interface (into a Docker bridge) rather than on
    /// destination address. Matching RFC1918 destinations — the widespread recipe — would also drop a
    /// box's unrelated forwarding, breaking a VPN or router that happens to run on the same host.</item>
    /// <item><c>DOCKER-USER</c> jumps to ufw's own <c>ufw-user-forward</c> first, so opening a
    /// container port later is plain ufw (<c>ufw route allow</c>) rather than a Rask-specific ritual.</item>
    /// </list>
    /// </summary>
    private static void AddDockerFirewall(HostFacts facts, BootstrapOptions options, List<BootstrapStep> risky, List<string> warnings)
    {
        // The ports to allow are the CONTAINER's, not the host's — DNAT has already rewritten the
        // destination by the time DOCKER-USER sees the packet. In domain mode the only published
        // container is Caddy (80:80 and 443:443, so the numbers coincide); in port mode it's the app,
        // whose --port maps to --container-port.
        int[] allowed;
        if (options.PublishedPort is null)
        {
            allowed = [80, 443];
        }
        else if (options.ContainerPort is { } containerPort)
        {
            allowed = [containerPort];
        }
        else
        {
            // We know a port is published but not what it maps to inside the container, so we can't
            // tell which port to keep open. Denying by default here would take the app off the
            // internet — refuse the step instead of guessing at the cost of the thing being deployed.
            warnings.Add("Docker's published ports are left outside the firewall — Rask couldn't tell which container port this app's --port maps to, and a default-deny it can't aim would take the app offline.");
            return;
        }

        // What the block on the box would have to say to be the one we want. It carries the rule format
        // as well as the ports, so a Rask that changes the rules refreshes an old block rather than
        // trusting a port list that happens to match.
        var signature = $"{RulesVersion}:{string.Join(',', allowed.Select(p => p.ToString(CultureInfo.InvariantCulture)))}";
        if (string.Equals(facts.DockerFirewall, signature, StringComparison.Ordinal))
        {
            return; // already exactly this — the second and every later deploy stays a no-op
        }

        var rules = string.Join('\n', allowed.Select(p =>
            $"-A {DockerChain} -p tcp -m tcp --dport {p.ToString(CultureInfo.InvariantCulture)} -j RETURN"));
        var opened = string.Join(", ", allowed.Select(p => p.ToString(CultureInfo.InvariantCulture)));

        risky.Add(new BootstrapStep(
            $"Make Docker's published ports obey the firewall (allow {opened}; deny every other container port)",
            Privileged(DockerFirewallScript(signature, rules), facts.IsRoot),
            // Strip our block and reload, then tear the chain down in the live ruleset too — a reload
            // alone leaves the already-loaded rules in place. No single quotes anywhere: this string is
            // spliced into the guard's own `sh -c '…'` (see ArmGuardScript).
            Undo: $"sed -i /{BlockBegin}/,/{BlockEnd}/d {AfterRules}; ufw reload >/dev/null 2>&1 || true; "
                + $"iptables -D DOCKER-USER -j {DockerChain} 2>/dev/null || true; iptables -D DOCKER-USER -j ufw-user-forward 2>/dev/null || true; "
                + $"iptables -F {DockerChain} 2>/dev/null || true; iptables -X {DockerChain} 2>/dev/null || true;"));
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

    /// <summary>
    /// Install (or refresh) the <c>DOCKER-USER</c> block in <see cref="AfterRules"/> and load it the
    /// way ufw does. The whole block is regenerated every time and fenced by markers, so a re-deploy
    /// replaces it exactly rather than appending — and the <c>-F</c> lines make loading it twice
    /// produce the same ruleset as loading it once (ufw restores with <c>--noflush</c>, so a chain we
    /// only appended to would grow a duplicate set on every reload).
    ///
    /// <para>A copy is kept until ufw has accepted the file. A rules file ufw rejects fails the reload
    /// and would leave the box with no firewall at all, so the failure path puts the original back and
    /// reloads again before reporting — never a half-applied firewall.</para>
    /// </summary>
    private static string DockerFirewallScript(string signature, string portRules) => $$"""
        set -e
        [ -f {{AfterRules}} ] || { echo "rask: {{AfterRules}} is missing, so there is no ufw to put Docker's published ports behind" >&2; exit 1; }
        cp {{AfterRules}} {{AfterRules}}.rask-bak
        awk '/^{{BlockBegin}}/{skip=1} skip!=1{print} /^{{BlockEnd}}/{skip=0}' {{AfterRules}}.rask-bak > {{AfterRules}}.rask-new
        cat >> {{AfterRules}}.rask-new <<'RASK_RULES'
        {{BlockBegin}} {{signature}} managed by rask deploy; this block is rewritten whenever it goes stale
        # Docker publishes a port with its own iptables rules, which are reached through FORWARD and so
        # never meet ufw's INPUT rules. DOCKER-USER is the hook Docker consults first and never writes
        # to itself: default-deny here is what makes `ufw deny` true for containers as well.
        *filter
        :DOCKER-USER - [0:0]
        :{{DockerChain}} - [0:0]
        -F DOCKER-USER
        -F {{DockerChain}}
        # ufw's own forward rules first, so `ufw route allow ...` is how you open another container port.
        -A DOCKER-USER -j ufw-user-forward
        -A DOCKER-USER -j {{DockerChain}}
        # Replies to connections a container opened stay allowed, or nothing could reach out.
        -A {{DockerChain}} -m conntrack --ctstate RELATED,ESTABLISHED -j RETURN
        # Traffic already inside the box (host, container-to-container) isn't "incoming".
        -A {{DockerChain}} -i lo -j RETURN
        -A {{DockerChain}} -i docker0 -j RETURN
        -A {{DockerChain}} -i br-+ -j RETURN
        # What this deploy publishes, on the CONTAINER port: PREROUTING has already rewritten it.
        {{portRules}}
        # Everything else being forwarded INTO a Docker bridge. Matching the outgoing interface rather
        # than an RFC1918 destination keeps this off a box's unrelated forwarding (a VPN, a router).
        -A {{DockerChain}} -o docker0 -j DROP
        -A {{DockerChain}} -o br-+ -j DROP
        COMMIT
        {{BlockEnd}}
        RASK_RULES
        chmod 640 {{AfterRules}}.rask-new
        mv {{AfterRules}}.rask-new {{AfterRules}}
        if ! ufw reload >/dev/null 2>&1; then
          mv {{AfterRules}}.rask-bak {{AfterRules}}
          ufw reload >/dev/null 2>&1 || true
          echo "rask: ufw wouldn't load the Docker rules — {{AfterRules}} has been put back as it was" >&2
          exit 1
        fi
        rm -f {{AfterRules}}.rask-bak
        iptables -C DOCKER-USER -j {{DockerChain}} 2>/dev/null || { echo "rask: the Docker firewall rules didn't take effect" >&2; exit 1; }
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
