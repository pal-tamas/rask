namespace Rask.Cli.Tests;

/// <summary>
/// <see cref="HostSetup"/> is what actually changes someone's production box. These tests drive the
/// whole flow through the process seam — no ssh, no host — and pin the one rule the design exists to
/// honour: <strong>never revoke the access you're currently using until a fresh connection proves the
/// replacement works.</strong>
/// </summary>
public class HostSetupTests
{
    private const string BareRootProbe = """
        user=root
        uid=0
        systemd=yes
        docker=no
        dockerok=no
        dockergroup=no
        sudo=root
        apt=yes
        ufw=no
        ufwactive=
        sshinclude=yes
        sshdread=yes
        sshport=22
        sshrootlogin=yes
        sshpasswordauth=yes
        sshkbdauth=yes
        end=ok
        """;

    private const string ReadyProbe = """
        user=deploy
        uid=1000
        systemd=yes
        docker=yes
        dockerok=yes
        dockergroup=yes
        sudo=yes
        apt=yes
        ufw=yes
        ufwactive=active
        sshinclude=yes
        sshdread=yes
        sshport=22
        sshrootlogin=no
        sshpasswordauth=no
        sshkbdauth=no
        end=ok
        """;

    private static BootstrapOptions FullSetup => new("deploy", Firewall: true, HardenSsh: true, PublishedPort: null);

    private static HostSetup Create(IConsole console, IProcessRunner runner) =>
        new(console, runner) { ReadinessDelay = TimeSpan.Zero, ReadinessAttempts = 1 };

    private static bool IsProbe(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[^1], HostProbe.ProbeScript, StringComparison.Ordinal);

    /// <summary>Either gate: the docker-capable check before the risky steps, or the reachability check after.</summary>
    private static bool IsVerify(IReadOnlyList<string> args) =>
        args.Count > 0
        && (string.Equals(args[^1], HostSetup.VerifyScript, StringComparison.Ordinal)
            || string.Equals(args[^1], HostSetup.ReachableScript, StringComparison.Ordinal));

    /// <summary>Every ssh script that was actually sent to the box.</summary>
    private static IEnumerable<string> ScriptsRun(FakeProcessRunner runner) =>
        runner.Invocations.Where(i => i.FileName == "ssh").Select(i => i.Arguments[^1]);

    /// <summary>
    /// Nothing on the box was changed. Asserts the property rather than a call count: reconnaissance
    /// (the read-only probe, and `ssh -G`, which resolves local config without connecting) is always
    /// allowed — running anything else is not.
    /// </summary>
    private static void AssertHostUntouched(FakeProcessRunner runner) =>
        Assert.All(runner.Invocations, i => Assert.True(
            IsProbe(i.Arguments) || i.Arguments.Contains("-G"),
            $"this should not have run against the host: {string.Join(' ', i.Arguments)}"));

    /// <summary>
    /// A host that answers the probe as <paramref name="probe"/> and succeeds at everything else.
    /// <paramref name="overrides"/> returns null to fall through to the default behaviour.
    /// </summary>
    private static FakeProcessRunner Host(string probe, Func<IReadOnlyList<string>, ProcessResult?>? overrides = null) =>
        new()
        {
            CaptureHandler = args =>
                overrides?.Invoke(args) is { } result ? result
                : IsProbe(args) ? new ProcessResult(0, probe, string.Empty)
                : IsVerify(args) ? new ProcessResult(0, "rask-ok\n", string.Empty)
                : new ProcessResult(0, string.Empty, string.Empty),
        };

    // ── A box that already runs Docker is deployable, full stop ─────────────────────────────────────

    /// <summary>
    /// A very common production shape: a least-privilege `deploy` login with NO sudo, no ufw (a cloud
    /// firewall does that job), sshd hardened by hand. Docker works — so `rask deploy` must deploy.
    /// </summary>
    private const string ReadyNoSudoNoUfwProbe = """
        user=deploy
        uid=1000
        systemd=yes
        docker=yes
        dockerok=yes
        dockergroup=yes
        sudo=no
        apt=yes
        ufw=no
        ufwactive=
        sshinclude=yes
        sshdread=yes
        sshport=22
        sshrootlogin=no
        sshpasswordauth=no
        sshkbdauth=no
        end=ok
        """;

    [Fact]
    public async Task A_working_box_deploys_even_when_we_cant_improve_it()
    {
        // Regression: setup exists to make a box deployable, not to gate a working deploy on being
        // allowed to add a firewall. A no-sudo deploy user behind a cloud firewall deployed fine
        // before host setup existed and must keep doing so.
        var console = new StringConsole();
        var runner = Host(ReadyNoSudoNoUfwProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        AssertHostUntouched(runner);
    }

    [Fact]
    public async Task A_working_box_without_a_firewall_is_never_nagged_about_it()
    {
        // Re-offering setup on every deploy to a box that's already serving is noise, not safety.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(ReadyNoSudoNoUfwProbe);

        await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.DoesNotContain("isn't ready", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("now?", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_host_still_improves_a_box_that_already_deploys()
    {
        // The escape hatch: --setup-host is an explicit "prepare this host", so a ready box that's
        // missing a firewall does get one.
        var probe = ReadyNoSudoNoUfwProbe.Replace("sudo=no", "sudo=yes", StringComparison.Ordinal);
        var console = new StringConsole();
        var runner = Host(probe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Forced, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        Assert.Contains(ScriptsRun(runner), s => s.Contains("ufw --force enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Setup_host_on_a_working_box_it_cant_touch_still_deploys()
    {
        // Asked to set up, unable to (no sudo), but the box deploys — say so and get on with it.
        var console = new StringConsole();
        var runner = Host(ReadyNoSudoNoUfwProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Forced, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        Assert.Contains("Skipped host setup", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_setup_host_deploys_to_a_working_box_without_complaint()
    {
        // What the generated CI workflow does on every push.
        var console = new StringConsole();
        var runner = Host(ReadyNoSudoNoUfwProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Disabled, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        Assert.Equal(string.Empty, console.ErrorText);
    }

    // ── The happy paths ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_ready_host_is_used_as_is_and_never_touched()
    {
        var runner = Host(ReadyProbe);

        var target = await Create(new StringConsole(), runner).EnsureReadyAsync(
            SshTarget.Parse("deploy@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        AssertHostUntouched(runner);
    }

    [Fact]
    public async Task A_bare_root_box_is_set_up_and_the_deploy_switches_to_the_new_login()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        // The caller must deploy as 'deploy', not the 'root' it was given — root SSH is now off.
        Assert.Equal("deploy@box", target?.ToString());

        var scripts = ScriptsRun(runner).ToList();
        Assert.Contains(scripts, s => s.Contains("get.docker.com", StringComparison.Ordinal));
        Assert.Contains(scripts, s => s.Contains("useradd -m -s /bin/bash deploy", StringComparison.Ordinal));
        Assert.Contains(scripts, s => s.Contains("ufw --force enable", StringComparison.Ordinal));
        Assert.Contains(scripts, s => s.Contains("PermitRootLogin no", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_risky_steps_run_only_after_the_new_login_is_proved_and_only_behind_the_guard()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        var scripts = ScriptsRun(runner).ToList();
        var createUser = scripts.FindIndex(s => s.Contains("useradd", StringComparison.Ordinal));
        var verify = scripts.FindIndex(s => s == HostSetup.VerifyScript);
        var arm = scripts.FindIndex(s => s.Contains("systemd-run", StringComparison.Ordinal));
        var firewall = scripts.FindIndex(s => s.Contains("ufw --force enable", StringComparison.Ordinal));
        var harden = scripts.FindIndex(s => s.Contains("PermitRootLogin no", StringComparison.Ordinal));
        var disarm = scripts.FindIndex(s => s.Contains("touch /run/rask-setup-ok", StringComparison.Ordinal));

        Assert.True(createUser < verify, "the new login must exist before we try it");
        Assert.True(verify < arm, "we must prove the new login works before anything risky");
        Assert.True(arm < firewall, "the guard must be armed before the firewall");
        Assert.True(firewall < harden, "SSH hardening is last — it's the change that can't be undone from outside");
        Assert.True(harden < disarm, "the guard is only disarmed once everything risky has landed");
    }

    [Fact]
    public async Task Verification_opens_a_brand_new_connection()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        // A reused ControlMaster channel would still be authenticated as root and prove nothing.
        var verify = runner.Invocations.First(i => IsVerify(i.Arguments));
        Assert.Contains("ControlPath=none", verify.Arguments);
        Assert.Contains("deploy@box", verify.Arguments);
    }

    // ── The gate: never revoke access we haven't replaced ────────────────────────────────────────────

    [Fact]
    public async Task A_new_login_that_doesnt_work_stops_everything_before_anything_risky()
    {
        // THE critical test. If we can't get in as 'deploy', hardening would lock us out permanently.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args => IsVerify(args) ? new ProcessResult(255, string.Empty, "Permission denied") : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        var scripts = ScriptsRun(runner).ToList();
        Assert.DoesNotContain(scripts, s => s.Contains("PermitRootLogin", StringComparison.Ordinal));
        Assert.DoesNotContain(scripts, s => s.Contains("ufw --force enable", StringComparison.Ordinal));
        Assert.DoesNotContain(scripts, s => s.Contains("systemd-run", StringComparison.Ordinal));
        Assert.Contains("firewall and SSH config are untouched", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Losing_the_box_after_hardening_leaves_the_guard_armed_and_says_so()
    {
        // Verification passes once (after the user is created) then fails (after hardening).
        var verifies = 0;
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            IsVerify(args) ? (++verifies == 1 ? new ProcessResult(0, "rask-ok\n", string.Empty) : new ProcessResult(255, string.Empty, "timeout"))
            : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        // Disarming a guard we can't reach through would be a lie; the box heals itself instead.
        Assert.DoesNotContain(ScriptsRun(runner), s => s.Contains("touch /run/rask-setup-ok", StringComparison.Ordinal));
        Assert.Contains("Locked out", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(HostBootstrap.GuardDelay, console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_risky_step_that_left_us_reachable_disarms_the_guard()
    {
        // No lockout happened, so there's nothing for the guard to heal — keep what worked.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            args[^1].Contains("ufw --force enable", StringComparison.Ordinal) ? new ProcessResult(1, string.Empty, "ufw: command failed") : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.Contains(ScriptsRun(runner), s => s.Contains("touch /run/rask-setup-ok", StringComparison.Ordinal));
        Assert.Contains("Host setup failed at: Enable the firewall", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("still reachable", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nothing_risky_runs_if_the_guard_cant_be_armed()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            args[^1].Contains("systemd-run", StringComparison.Ordinal) ? new ProcessResult(1, string.Empty, "no systemd-run") : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.DoesNotContain(ScriptsRun(runner), s => s.Contains("ufw --force enable", StringComparison.Ordinal));
        Assert.DoesNotContain(ScriptsRun(runner), s => s.Contains("PermitRootLogin", StringComparison.Ordinal));
        Assert.Contains("refusing to touch the firewall", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_preparation_step_stops_the_deploy()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            args[^1].Contains("get.docker.com", StringComparison.Ordinal) ? new ProcessResult(1, string.Empty, "curl: (6) Could not resolve host") : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.Contains("Could not resolve host", console.ErrorText, StringComparison.Ordinal); // the box's own words
        Assert.DoesNotContain(ScriptsRun(runner), s => s.Contains("useradd", StringComparison.Ordinal));
    }

    // ── The guard must actually be disarmed ─────────────────────────────────────────────────────────

    [Fact]
    public async Task A_disarm_that_never_lands_is_reported_rather_than_called_ready()
    {
        // If the disarm doesn't land, the guard fires in ~5min and quietly undoes the firewall and
        // hardening. Printing "Host ready" and deploying would leave the user believing their box is
        // hardened while it silently reverts underneath them.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            args[^1].Contains("touch /run/rask-setup-ok", StringComparison.Ordinal) ? new ProcessResult(255, string.Empty, "connection reset") : null);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.DoesNotContain("Host ready", console.OutText, StringComparison.Ordinal);
        Assert.Contains("rollback guard", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(HostBootstrap.GuardDelay, console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_post_hardening_check_asks_only_whether_were_still_in()
    {
        // A flaky Docker daemon is not a lockout. Docker was already proved before anything risky ran,
        // so re-testing it here would roll back a firewall and hardening that were perfectly fine.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        var scripts = ScriptsRun(runner).ToList();
        var harden = scripts.FindIndex(s => s.Contains("PermitRootLogin no", StringComparison.Ordinal));
        var reachable = scripts.FindIndex(harden + 1, s => s == HostSetup.ReachableScript);
        Assert.True(reachable > harden, "the post-hardening gate must be a plain reachability check");
    }

    [Fact]
    public async Task The_firewall_opens_the_port_ssh_is_actually_using()
    {
        // Resolved locally with `ssh -G`, which reads ~/.ssh/config — so it knows the port behind an
        // alias, and catches a socket-activated sshd whose sshd -T Port is a lie.
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe, args =>
            args.Contains("-G") ? new ProcessResult(0, "user root\nhostname box\nport 2222\n", string.Empty) : null);

        await Create(console, runner).EnsureReadyAsync(SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Contains(ScriptsRun(runner), s => s.Contains("ufw allow 2222/tcp", StringComparison.Ordinal));
    }

    // ── Consent ─────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task A_terminal_is_shown_the_plan_and_asked_first()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        await Create(console, runner).EnsureReadyAsync(SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Contains("isn't ready to deploy to", console.OutText, StringComparison.Ordinal);
        Assert.Contains("Install Docker", console.OutText, StringComparison.Ordinal);
        Assert.Contains("Set up root@box now?", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Answering_no_leaves_the_host_alone()
    {
        var console = new StringConsole { InputLines = ["n"] };
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        AssertHostUntouched(runner);
        Assert.Contains("Left the host alone", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_terminal_it_refuses_to_change_the_host_and_says_how_to_allow_it()
    {
        // Piped/CI: there's nobody to ask, and a production box is not something to change on a guess.
        var console = new StringConsole(); // IsInputRedirected stays true
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        AssertHostUntouched(runner);
        Assert.Contains("--setup-host", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Setup_host_proceeds_without_asking()
    {
        var console = new StringConsole(); // no terminal, no scripted input
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Forced, CancellationToken.None);

        Assert.Equal("deploy@box", target?.ToString());
        Assert.Contains(ScriptsRun(runner), s => s.Contains("get.docker.com", StringComparison.Ordinal));
    }

    [Fact]
    public async Task No_setup_host_refuses_and_still_explains_what_the_box_needs()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(BareRootProbe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Disabled, CancellationToken.None);

        Assert.Null(target);
        AssertHostUntouched(runner);
        Assert.Contains("Install Docker", console.OutText, StringComparison.Ordinal); // still tells you what's missing
        Assert.Contains("--no-setup-host", console.ErrorText, StringComparison.Ordinal);
    }

    // ── Refusals ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unreachable_host_fails_before_any_prompt()
    {
        var console = new StringConsole { InputLines = ["y"] };
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(255, string.Empty, "Host key verification failed.") };

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.Contains("Host key verification failed", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_who_cant_sudo_gets_told_rather_than_a_wall_of_permission_errors()
    {
        var probe = BareRootProbe.Replace("uid=0", "uid=1000", StringComparison.Ordinal).Replace("sudo=root", "sudo=no", StringComparison.Ordinal);
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(probe);

        var target = await Create(console, runner).EnsureReadyAsync(
            SshTarget.Parse("alice@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Null(target);
        Assert.Contains("neither root nor able to run sudo", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Warnings_about_what_we_refused_to_do_reach_the_user()
    {
        // A silently-skipped firewall reads to the user as a firewall. Say it out loud.
        var probe = BareRootProbe.Replace("sshport=22", string.Empty, StringComparison.Ordinal);
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(probe);

        await Create(console, runner).EnsureReadyAsync(SshTarget.Parse("root@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.Contains("Firewall skipped", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(ScriptsRun(runner), s => s.Contains("ufw --force enable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Setup_host_still_reports_what_it_refused_to_do()
    {
        // --setup-host skips the prompt, not the disclosure — the prompt was the only other thing
        // that would have printed these.
        var probe = BareRootProbe.Replace("sshport=22", string.Empty, StringComparison.Ordinal);
        var console = new StringConsole();
        var runner = Host(probe);

        await Create(console, runner).EnsureReadyAsync(SshTarget.Parse("root@box"), FullSetup, SetupMode.Forced, CancellationToken.None);

        Assert.Contains("Firewall skipped", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_user_who_cant_sudo_is_told_before_being_asked_to_approve_a_plan()
    {
        // Confirming a plan we can't carry out wastes the user's decision.
        var probe = BareRootProbe.Replace("uid=0", "uid=1000", StringComparison.Ordinal).Replace("sudo=root", "sudo=no", StringComparison.Ordinal);
        var console = new StringConsole { InputLines = ["y"] };
        var runner = Host(probe);

        await Create(console, runner).EnsureReadyAsync(SshTarget.Parse("alice@box"), FullSetup, SetupMode.Ask, CancellationToken.None);

        Assert.DoesNotContain("now?", console.OutText, StringComparison.Ordinal); // never got as far as prompting
        Assert.Contains("neither root nor able to run sudo", console.ErrorText, StringComparison.Ordinal);
    }
}
