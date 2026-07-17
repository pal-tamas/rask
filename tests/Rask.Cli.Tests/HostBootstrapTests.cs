namespace Rask.Cli.Tests;

/// <summary>
/// <see cref="HostBootstrap.Plan"/> decides what <c>rask deploy</c> does to someone's production box:
/// installs Docker, creates a login, enables a firewall, disables root SSH. It's a pure function of the
/// probe's facts, so every one of those decisions is pinned here — including the ones where the correct
/// answer is "refuse".
/// </summary>
public class HostBootstrapPlanTests
{
    /// <summary>A fresh VPS: root over SSH, nothing installed, stock permissive sshd.</summary>
    private static HostFacts BareRoot(params int[] sshPorts) => new(
        User: "root", IsRoot: true, HasSystemd: true,
        DockerInstalled: false, DockerUsable: false, InDockerGroup: false, CanSudo: true,
        HasApt: true,
        UfwInstalled: false, UfwActive: false, SshPorts: sshPorts.Length == 0 ? [22] : sshPorts,
        SshConfigInclude: true, SshdReadable: true,
        SshRootLoginPermitted: true, SshPasswordAuthEnabled: true, SshKbdAuthEnabled: true,
        Complete: true);

    /// <summary>A box that's already fully set up — the second and every later deploy.</summary>
    private static HostFacts Ready() => new(
        User: "deploy", IsRoot: false, HasSystemd: true,
        DockerInstalled: true, DockerUsable: true, InDockerGroup: true, CanSudo: true,
        HasApt: true,
        UfwInstalled: true, UfwActive: true, SshPorts: [22],
        SshConfigInclude: true, SshdReadable: true,
        SshRootLoginPermitted: false, SshPasswordAuthEnabled: false, SshKbdAuthEnabled: false,
        Complete: true);

    private static BootstrapOptions FullSetup(int? port = null) => new("deploy", Firewall: true, HardenSsh: true, PublishedPort: port);

    private static string AllScripts(BootstrapPlan plan) => string.Join('\n', plan.AllSteps.Select(s => s.Script));

    [Fact]
    public void A_ready_host_needs_nothing_done_to_it()
    {
        // The idempotency guarantee: every deploy after the first must be a no-op on the host.
        var plan = HostBootstrap.Plan(Ready(), FullSetup());

        Assert.True(plan.IsEmpty);
        Assert.Null(plan.NewUser);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void A_bare_root_box_gets_the_full_treatment_in_the_mandated_order()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup());

        // Preparation can't cost you access; Risky can. The split is the safety contract.
        Assert.Equal(
            ["Install Docker", "Start the Docker daemon", "Create the 'deploy' login and give it Docker access"],
            plan.Preparation.Select(s => s.Description));
        Assert.Equal(
            ["Enable the firewall (allow 22, 80, 443; deny everything else inbound)", "Harden SSH (disable SSH password login and root login)"],
            plan.Risky.Select(s => s.Description));
        Assert.Equal("deploy", plan.NewUser);
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void Docker_is_installed_from_dockers_own_script_downloaded_before_it_runs()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup());
        var install = plan.Preparation[0].Script;

        // Downloaded then run, never `curl | sh` — a truncated download must not half-execute.
        Assert.Contains("curl -fsSL https://get.docker.com -o /tmp/rask-get-docker.sh", install, StringComparison.Ordinal);
        Assert.Contains("sh /tmp/rask-get-docker.sh", install, StringComparison.Ordinal);
        Assert.DoesNotContain("| sh", install, StringComparison.Ordinal);
    }

    [Fact]
    public void An_existing_docker_isnt_reinstalled()
    {
        var facts = BareRoot() with { DockerInstalled = true, DockerUsable = true };

        var plan = HostBootstrap.Plan(facts, FullSetup());

        Assert.DoesNotContain(plan.AllSteps, s => s.Description == "Install Docker");
        Assert.DoesNotContain("get.docker.com", AllScripts(plan), StringComparison.Ordinal);
    }

    // ── The deploy user ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_deploy_user_gets_the_deploying_users_keys_or_the_step_fails_loudly()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup());
        var create = plan.Preparation[2].Script;

        // A login with no authorized_keys is a login you can't use — and hardening would then lock us
        // out for good. Refuse rather than create a useless account.
        Assert.Contains("getent passwd root | cut -d: -f6", create, StringComparison.Ordinal);
        Assert.Contains("[ -f \"$SRC\" ] ||", create, StringComparison.Ordinal);
        Assert.Contains("exit 1", create, StringComparison.Ordinal);
        Assert.Contains("usermod -aG docker deploy", create, StringComparison.Ordinal);
    }

    [Fact]
    public void The_sudoers_file_is_validated_before_it_can_break_sudo()
    {
        var create = HostBootstrap.Plan(BareRoot(), FullSetup()).Preparation[2].Script;

        // A malformed file in /etc/sudoers.d can break sudo entirely. Write to a dotted name (which
        // sudo ignores), validate, then move into place.
        Assert.Contains("visudo -c -f /etc/sudoers.d/rask-deploy.tmp", create, StringComparison.Ordinal);
        var validate = create.IndexOf("visudo -c", StringComparison.Ordinal);
        var move = create.IndexOf("mv /etc/sudoers.d/rask-deploy.tmp", StringComparison.Ordinal);
        Assert.True(validate < move, "sudoers must be validated before being moved into place");
    }

    [Fact]
    public void No_deploy_user_is_created_when_were_already_a_non_root_login()
    {
        // An existing non-root login already is what the deploy user would be — creating a second is noise.
        var facts = Ready() with { InDockerGroup = false, DockerUsable = false };

        var plan = HostBootstrap.Plan(facts, FullSetup());

        Assert.Null(plan.NewUser);
        Assert.Contains(plan.Preparation, s => s.Description == "Add 'deploy' to the docker group");
        Assert.DoesNotContain("useradd", AllScripts(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Opting_out_of_the_deploy_user_keeps_the_current_login()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup() with { DeployUser = null });

        Assert.Null(plan.NewUser);
        Assert.DoesNotContain("useradd", AllScripts(plan), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("deploy; rm -rf /")]
    [InlineData("deploy$(whoami)")]
    [InlineData("deploy user")]
    [InlineData("Deploy")]     // useradd's portable set is lower-case only
    [InlineData("1deploy")]    // must not start with a digit
    [InlineData("")]
    [InlineData("averyveryveryverylongusernamethatexceedsthelimit")]
    public void An_unusable_deploy_user_name_is_rejected_before_we_ever_connect(string name)
    {
        // The name reaches a remote shell. Reject it up front rather than escape it and hope.
        Assert.False(HostBootstrap.IsValidUserName(name));
        Assert.Throws<ArgumentException>(() => HostBootstrap.Plan(BareRoot(), FullSetup() with { DeployUser = name }));
    }

    [Theory]
    [InlineData("deploy")]
    [InlineData("_svc")]
    [InlineData("rask-deploy")]
    [InlineData("app1")]
    public void A_valid_user_name_is_accepted(string name) => Assert.True(HostBootstrap.IsValidUserName(name));

    // ── The firewall ────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_firewall_allows_every_port_sshd_actually_listens_on()
    {
        // Read off the box, never guessed — sshd on 2222 plus a legacy 22 must both survive.
        var plan = HostBootstrap.Plan(BareRoot(2222, 22), FullSetup());
        var firewall = plan.Risky[0].Script;

        Assert.Contains("ufw allow 22/tcp", firewall, StringComparison.Ordinal);
        Assert.Contains("ufw allow 2222/tcp", firewall, StringComparison.Ordinal);
    }

    [Fact]
    public void The_firewall_refuses_to_enable_when_the_ssh_port_is_unknown()
    {
        // THE lockout guard. Not knowing which port to keep open means not touching the firewall.
        var plan = HostBootstrap.Plan(BareRoot() with { SshPorts = [] }, FullSetup());

        Assert.DoesNotContain("ufw", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains(plan.Warnings, w => w.Contains("Firewall skipped", StringComparison.Ordinal) && w.Contains("lock you out", StringComparison.Ordinal));
    }

    [Fact]
    public void Allow_rules_always_come_before_the_firewall_is_enabled()
    {
        var firewall = HostBootstrap.Plan(BareRoot(), FullSetup()).Risky[0].Script;

        // Enable-before-allow is the classic way to lock yourself out of your own box.
        var lastAllow = firewall.LastIndexOf("ufw allow", StringComparison.Ordinal);
        var deny = firewall.IndexOf("ufw default deny incoming", StringComparison.Ordinal);
        var enable = firewall.IndexOf("ufw --force enable", StringComparison.Ordinal);
        Assert.True(lastAllow < deny, "every allow rule must precede the default-deny policy");
        Assert.True(deny < enable, "the policy must be set before the firewall is enabled");
    }

    [Fact]
    public void Domain_mode_opens_the_ports_caddy_needs_for_https_and_acme()
    {
        var firewall = HostBootstrap.Plan(BareRoot(), FullSetup(port: null)).Risky[0].Script;

        Assert.Contains("ufw allow 80/tcp", firewall, StringComparison.Ordinal);  // ACME HTTP challenge
        Assert.Contains("ufw allow 443/tcp", firewall, StringComparison.Ordinal);
    }

    [Fact]
    public void Port_mode_opens_the_published_port_instead_of_80_and_443()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup(port: 8080));
        var firewall = plan.Risky[0].Script;

        Assert.Contains("ufw allow 8080/tcp", firewall, StringComparison.Ordinal);
        Assert.DoesNotContain("ufw allow 443/tcp", firewall, StringComparison.Ordinal);
        Assert.Contains("allow 22, 8080", plan.Risky[0].Description, StringComparison.Ordinal);
    }

    [Fact]
    public void A_box_with_no_way_to_install_ufw_skips_the_firewall_instead_of_failing_the_deploy()
    {
        // Fedora/RHEL/Alpine run Docker fine but have no ufw and no apt-get. Planning a step whose
        // script can only `exit 1` would abort the deploy on a box that's otherwise perfectly ready —
        // and re-running would reproduce it forever, until the user discovered --no-firewall.
        var facts = BareRoot() with { UfwInstalled = false, HasApt = false };

        var plan = HostBootstrap.Plan(facts, FullSetup());

        Assert.DoesNotContain("ufw", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains(plan.Warnings, w => w.Contains("Firewall skipped", StringComparison.Ordinal) && w.Contains("apt-get", StringComparison.Ordinal));
        // The rest of the setup still happens — the box just doesn't get a ufw.
        Assert.Contains(plan.Preparation, s => s.Description == "Install Docker");
    }

    [Fact]
    public void Ufw_is_only_installed_when_its_actually_missing()
    {
        var facts = BareRoot() with { UfwInstalled = true, UfwActive = false }; // installed but off

        var firewall = HostBootstrap.Plan(facts, FullSetup()).Risky[0].Script;

        Assert.DoesNotContain("apt-get install", firewall, StringComparison.Ordinal);
        Assert.Contains("ufw --force enable", firewall, StringComparison.Ordinal);
    }

    [Fact]
    public void The_firewall_always_allows_the_port_were_actually_connected_on()
    {
        // sshd -T reports sshd_config's Port, which is NOT the listening port under systemd socket
        // activation — trusting it alone would firewall off the very port this session is using.
        var plan = HostBootstrap.Plan(BareRoot(22), FullSetup() with { ConnectPort = 2222 });

        var firewall = plan.Risky[0].Script;
        Assert.Contains("ufw allow 2222/tcp", firewall, StringComparison.Ordinal);
        Assert.Contains("ufw allow 22/tcp", firewall, StringComparison.Ordinal); // and what sshd -T said
    }

    [Fact]
    public void The_connect_port_alone_is_enough_to_configure_a_firewall()
    {
        // sshd -T unreadable, but we know the port we're on — that's the one that must stay open.
        var plan = HostBootstrap.Plan(BareRoot() with { SshPorts = [] }, FullSetup() with { ConnectPort = 2222 });

        Assert.Contains("ufw allow 2222/tcp", plan.Risky[0].Script, StringComparison.Ordinal);
        Assert.DoesNotContain(plan.Warnings, w => w.Contains("Firewall skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void An_already_firewalled_box_is_left_alone()
    {
        // The box's existing rules are the user's business, not ours to overwrite.
        var plan = HostBootstrap.Plan(BareRoot() with { UfwInstalled = true, UfwActive = true }, FullSetup());

        Assert.DoesNotContain("ufw", AllScripts(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void An_already_firewalled_box_doesnt_warn_about_ports_it_no_longer_needs()
    {
        // Nothing to do, so nothing to say — otherwise a healthy box nags on every single deploy.
        var facts = BareRoot() with { UfwInstalled = true, UfwActive = true, SshPorts = [] };

        var plan = HostBootstrap.Plan(facts, FullSetup());

        Assert.DoesNotContain(plan.Warnings, w => w.Contains("Firewall skipped", StringComparison.Ordinal));
    }

    [Fact]
    public void Opting_out_of_the_firewall_leaves_it_untouched()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup() with { Firewall = false });

        Assert.DoesNotContain("ufw", AllScripts(plan), StringComparison.Ordinal);
    }

    // ── SSH hardening ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Root_login_is_only_disabled_once_a_non_root_login_exists_to_replace_it()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup());

        Assert.Equal("deploy", plan.NewUser);
        Assert.Contains("PermitRootLogin no", AllScripts(plan), StringComparison.Ordinal);
    }

    [Fact]
    public void Root_login_stays_enabled_when_root_is_still_the_login_we_deploy_as()
    {
        // Without a deploy user we'd be pulling up the only ladder we're standing on.
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup() with { DeployUser = null });

        Assert.DoesNotContain("PermitRootLogin no", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains("PasswordAuthentication no", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains(plan.Warnings, w => w.Contains("Root SSH login left enabled", StringComparison.Ordinal));
    }

    [Fact]
    public void Hardening_is_skipped_when_the_drop_in_would_be_silently_ignored()
    {
        // Without the Include line our file changes nothing. Writing it and reporting success is a lie.
        var plan = HostBootstrap.Plan(BareRoot() with { SshConfigInclude = false }, FullSetup());

        Assert.DoesNotContain("sshd_config.d/99-rask.conf", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains(plan.Warnings, w => w.Contains("SSH hardening skipped", StringComparison.Ordinal) && w.Contains("Include", StringComparison.Ordinal));
    }

    [Fact]
    public void Hardening_validates_the_config_before_reloading_and_verifies_it_applied()
    {
        var harden = HostBootstrap.Plan(BareRoot(), FullSetup()).Risky[^1].Script;

        var validate = harden.IndexOf("sshd -t", StringComparison.Ordinal);
        var reload = harden.IndexOf("systemctl reload ssh", StringComparison.Ordinal);
        var verify = harden.IndexOf("sshd -T | grep", StringComparison.Ordinal);
        Assert.True(validate < reload, "sshd -t must gate the reload");
        Assert.True(reload < verify, "the effective config is only meaningful after the reload");
        // grep -i matters: sshd -T prints its keywords lower-cased.
        Assert.Contains("grep -qix 'PermitRootLogin no'", harden, StringComparison.Ordinal);
    }

    [Fact]
    public void An_already_hardened_box_gets_no_hardening_step()
    {
        // Idempotency: re-hardening every deploy would re-prompt the user forever.
        var facts = BareRoot() with { SshRootLoginPermitted = false, SshPasswordAuthEnabled = false, SshKbdAuthEnabled = false };

        var plan = HostBootstrap.Plan(facts, FullSetup());

        Assert.DoesNotContain(plan.AllSteps, s => s.Description.StartsWith("Harden SSH", StringComparison.Ordinal));
    }

    [Fact]
    public void The_drop_in_always_carries_the_full_desired_set_not_just_whats_missing()
    {
        // Our own file is the source of these settings. Writing only the missing one would revert the
        // others the previous run established.
        var facts = BareRoot() with { SshPasswordAuthEnabled = false, SshKbdAuthEnabled = false };

        var harden = HostBootstrap.Plan(facts, FullSetup()).Risky[^1].Script;

        Assert.Contains("PasswordAuthentication no", harden, StringComparison.Ordinal);
        Assert.Contains("KbdInteractiveAuthentication no", harden, StringComparison.Ordinal);
        Assert.Contains("PermitRootLogin no", harden, StringComparison.Ordinal);
    }

    [Fact]
    public void Hardening_is_skipped_when_sshds_effective_config_couldnt_be_read()
    {
        // No sshd -T means we can't tell what's set, and couldn't prove a change applied. Don't guess.
        var plan = HostBootstrap.Plan(BareRoot() with { SshdReadable = false }, FullSetup());

        Assert.DoesNotContain("sshd_config.d", AllScripts(plan), StringComparison.Ordinal);
        Assert.Contains(plan.Warnings, w => w.Contains("SSH hardening skipped", StringComparison.Ordinal) && w.Contains("verify", StringComparison.Ordinal));
    }

    [Fact]
    public void Opting_out_of_hardening_leaves_sshd_alone()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup() with { HardenSsh = false });

        Assert.DoesNotContain("sshd", AllScripts(plan), StringComparison.Ordinal);
        Assert.DoesNotContain("PermitRootLogin", AllScripts(plan), StringComparison.Ordinal);
    }
}

/// <summary>
/// The rollback guard and the privilege/quoting helpers. The guard runs as root on a timer with nobody
/// watching, so what it will and won't touch is the whole ballgame.
/// </summary>
public class HostBootstrapGuardTests
{
    /// <summary>A fresh VPS: root over SSH, nothing installed, stock permissive sshd.</summary>
    private static HostFacts BareRoot() => new(
        User: "root", IsRoot: true, HasSystemd: true,
        DockerInstalled: false, DockerUsable: false, InDockerGroup: false, CanSudo: true,
        HasApt: true, UfwInstalled: false, UfwActive: false, SshPorts: [22],
        SshConfigInclude: true, SshdReadable: true,
        SshRootLoginPermitted: true, SshPasswordAuthEnabled: true, SshKbdAuthEnabled: true,
        Complete: true);

    private static BootstrapOptions FullSetup => new("deploy", Firewall: true, HardenSsh: true, PublishedPort: null);

    [Fact]
    public void The_guard_reverts_both_risky_changes_and_keys_on_a_sentinel_not_a_pid()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup);

        var arm = HostBootstrap.ArmGuardScript(plan, isRoot: true);

        // A PID-tracked guard dies with the client — precisely when we most need it to fire.
        Assert.Contains("systemd-run --unit=rask-rollback --on-active=5min", arm, StringComparison.Ordinal);
        Assert.Contains("[ -f /run/rask-setup-ok ]", arm, StringComparison.Ordinal);
        Assert.Contains("ufw --force disable", arm, StringComparison.Ordinal);
        Assert.Contains("rm -f /etc/ssh/sshd_config.d/99-rask.conf", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void The_guard_never_disables_a_firewall_this_run_didnt_enable()
    {
        // The user already runs ufw, so no firewall step is planned. If an interrupted run let the
        // guard fire, a fixed guard body would `ufw --force disable` THEIR firewall and expose the box.
        var facts = BareRoot() with { UfwInstalled = true, UfwActive = true };
        var plan = HostBootstrap.Plan(facts, FullSetup);

        var arm = HostBootstrap.ArmGuardScript(plan, isRoot: true);

        Assert.DoesNotContain("ufw", arm, StringComparison.Ordinal);
        Assert.Contains("rm -f /etc/ssh/sshd_config.d/99-rask.conf", arm, StringComparison.Ordinal); // still reverts what it DID do
    }

    [Fact]
    public void The_guard_never_removes_an_sshd_drop_in_this_run_didnt_write()
    {
        // Already hardened (by an earlier deploy), only the firewall is planned. A fixed guard body
        // would rm that drop-in and reload sshd — silently re-enabling password and root login on a box
        // the user believes is hardened.
        var facts = BareRoot() with { SshRootLoginPermitted = false, SshPasswordAuthEnabled = false, SshKbdAuthEnabled = false };
        var plan = HostBootstrap.Plan(facts, FullSetup);

        var arm = HostBootstrap.ArmGuardScript(plan, isRoot: true);

        Assert.DoesNotContain("99-rask.conf", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("reload ssh", arm, StringComparison.Ordinal);
        Assert.Contains("ufw --force disable", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_risky_step_carries_an_undo_so_the_guard_can_be_built_from_the_plan()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup);

        Assert.NotEmpty(plan.Risky);
        Assert.All(plan.Risky, s => Assert.False(string.IsNullOrWhiteSpace(s.Undo), $"'{s.Description}' can revoke access but has no undo"));
        // Preparation is unguarded by design — it can't cost you the box, so it needs no undo.
        Assert.All(plan.Preparation, s => Assert.Null(s.Undo));
    }

    [Fact]
    public void Arming_clears_a_stale_sentinel_and_a_leftover_timer()
    {
        var plan = HostBootstrap.Plan(BareRoot(), FullSetup);

        var arm = HostBootstrap.ArmGuardScript(plan, isRoot: true);

        // A sentinel left by a previous run would disarm this one before it ever armed.
        Assert.StartsWith("rm -f /run/rask-setup-ok", arm, StringComparison.Ordinal);
        Assert.Contains("systemctl stop rask-rollback.timer", arm, StringComparison.Ordinal);
        Assert.Contains("systemctl reset-failed rask-rollback.service", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void Disarming_writes_the_sentinel_and_stops_the_timer()
    {
        var disarm = HostBootstrap.DisarmGuardScript(isRoot: true);

        Assert.Contains("touch /run/rask-setup-ok", disarm, StringComparison.Ordinal);
        Assert.Contains("systemctl stop rask-rollback.timer", disarm, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_runs_scripts_directly_and_everyone_else_goes_through_sudo()
    {
        Assert.Equal("whoami", HostBootstrap.Privileged("whoami", isRoot: true));
        Assert.Equal("sudo -n sh -c 'whoami'", HostBootstrap.Privileged("whoami", isRoot: false));
    }

    [Fact]
    public void A_multi_line_script_survives_being_wrapped_for_sudo()
    {
        // The whole script has to arrive as one argument or `if` blocks break apart.
        var wrapped = HostBootstrap.Privileged("set -e\nif true; then echo hi; fi", isRoot: false);

        Assert.Equal("sudo -n sh -c 'set -e\nif true; then echo hi; fi'", wrapped);
    }

    [Theory]
    [InlineData("plain", "'plain'")]
    [InlineData("it's", "'it'\\''s'")]
    [InlineData("a'b'c", "'a'\\''b'\\''c'")]
    [InlineData("$(whoami)", "'$(whoami)'")]      // inside single quotes, nothing expands
    [InlineData("`id`", "'`id`'")]
    [InlineData("a\nb", "'a\nb'")]
    public void Shell_quoting_makes_every_character_literal(string input, string expected) =>
        Assert.Equal(expected, HostBootstrap.ShellQuote(input));
}
