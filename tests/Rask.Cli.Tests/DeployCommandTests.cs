using Rask.Cli;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

public sealed class DeployCommandTests
{
    private const string WorkingDir = "/proj";

    // ── Pure builders ───────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_arguments_target_the_remote_daemon_over_ssh()
    {
        var args = DeployCommand.BuildBuildArguments("deploy@box", "shop", "/proj/Dockerfile", "/proj");

        Assert.Equal(["-H", "ssh://deploy@box", "build", "-t", "shop:latest", "-f", "/proj/Dockerfile", "/proj"], args);
    }

    [Fact]
    public void Run_arguments_domain_mode_add_network_and_labels_no_published_port()
    {
        var args = DeployCommand.BuildRunArguments("deploy@box", "shop", "shop.example.com", "green", 8080, ["A=1"]);

        Assert.Equal(
        [
            "-H", "ssh://deploy@box", "run", "-d", "--name", "shop-green", "--restart", "unless-stopped",
            "--network", "rask", "--label", "rask.managed=true", "--label", "rask.app=shop",
            "--label", "rask.domain=shop.example.com", "--label", "rask.color=green", "-e", "A=1", "shop:latest",
        ], args);
    }

    [Fact]
    public void Run_arguments_port_mode_publish_the_host_port()
    {
        var args = DeployCommand.BuildRunArguments("deploy@box", "shop", domain: null, color: null, 9000, []);

        Assert.Equal(
        [
            "-H", "ssh://deploy@box", "run", "-d", "--name", "shop", "--restart", "unless-stopped",
            "-p", "9000:8080", "shop:latest",
        ], args);
    }

    [Theory]
    [InlineData(null, "blue")]
    [InlineData("green", "blue")]
    [InlineData("blue", "green")]
    public void NextColor_toggles_blue_green(string? current, string expected) =>
        Assert.Equal(expected, DeployCommand.NextColor(current));

    [Fact]
    public void ParseDeployedApps_reads_the_label_listing_and_normalizes_no_value()
    {
        var apps = DeployCommand.ParseDeployedApps("shop-blue\tshop\tshop.example.com\tblue\napi\tapi\t<no value>\t<no value>\n");

        Assert.Equal(2, apps.Count);
        Assert.Equal(new DeployedApp("shop-blue", "shop", "shop.example.com", "blue"), apps[0]);
        Assert.Equal(new DeployedApp("api", "api", string.Empty, string.Empty), apps[1]);
    }

    [Fact]
    public void BuildRoutingMap_forces_the_deploying_app_to_its_new_container_and_keeps_others()
    {
        IReadOnlyList<DeployedApp> apps =
        [
            new("shop-blue", "shop", "shop.example.com", "blue"),
            new("demo-blue", "demo", "demo.example.com", "blue"),
        ];

        var map = DeployCommand.BuildRoutingMap(apps, "demo", "demo.example.com", "demo-green");

        Assert.Equal("demo-green", map["demo.example.com"]); // deploying app → new container
        Assert.Equal("shop-blue", map["shop.example.com"]);  // other app kept
    }

    [Fact]
    public void BuildRoutingMap_skips_port_mode_apps_without_a_domain()
    {
        IReadOnlyList<DeployedApp> apps = [new("api", "api", string.Empty, string.Empty)];

        var map = DeployCommand.BuildRoutingMap(apps, "demo", "demo.example.com", "demo-blue");

        Assert.Single(map);
        Assert.False(map.ContainsKey(string.Empty));
    }

    [Fact]
    public void BuildCaddyfile_emits_a_block_per_route()
    {
        var caddyfile = DeployCommand.BuildCaddyfile(new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo.example.com"] = "demo-green",
            ["shop.example.com"] = "shop-blue",
        });

        Assert.Equal(
            "demo.example.com {\n\treverse_proxy demo-green:8080\n}\n\nshop.example.com {\n\treverse_proxy shop-blue:8080\n}\n",
            caddyfile);
    }

    [Theory]
    [InlineData("MyApp", "myapp")]
    [InlineData("My.Cool_App", "my.cool_app")]
    [InlineData("Company RaskServer", "company-raskserver")]
    [InlineData("--weird--", "weird")]
    public void ToContainerSlug_produces_a_docker_safe_name(string input, string expected) =>
        Assert.Equal(expected, DeployCommand.ToContainerSlug(input));

    // ── Orchestration (via the process/file seam) ───────────────────────────────────────────────────

    private static DeployCommand Create(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console)
    {
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        return new DeployCommand(console, fs, runner, WorkingDir) { ReadinessDelay = TimeSpan.Zero, ReadinessAttempts = 1 };
    }

    /// <summary>
    /// A host that's already set up, as the probe would report it. Every deploy test that isn't
    /// *about* host setup starts from one, so the bootstrap path stays a no-op and the assertions are
    /// about deploying.
    /// </summary>
    internal const string ReadyHostProbe = """
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

    /// <summary>The command's own option schema — the same one --help and completion render from.</summary>
    private static ArgumentSchema Schema() =>
        new DeployCommand(new StringConsole(), new FakeFileSystem(), new FakeProcessRunner(), WorkingDir).OptionSchema!;

    /// <summary>Either host-setup gate: docker-capable before the risky steps, reachable after.</summary>
    internal static bool IsHostVerify(IReadOnlyList<string> args) =>
        args.Count > 0
        && (string.Equals(args[^1], HostSetup.VerifyScript, StringComparison.Ordinal)
            || string.Equals(args[^1], HostSetup.ReachableScript, StringComparison.Ordinal));

    /// <summary>The host probe is the ssh invocation whose last argument is the probe script.</summary>
    internal static bool IsHostProbe(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[^1], HostProbe.ProbeScript, StringComparison.Ordinal);

    /// <summary>A capture handler for the deploy flow: a ready host, a given `docker ps` listing, container up.</summary>
    private static Func<IReadOnlyList<string>, ProcessResult> Captures(string psListing = "") => args =>
        IsHostProbe(args) ? new ProcessResult(0, ReadyHostProbe, string.Empty)
        : args.Contains("ps") ? new ProcessResult(0, psListing, string.Empty)
        : args.Contains("inspect") ? new ProcessResult(0, "true\n", string.Empty)
        : new ProcessResult(0, string.Empty, string.Empty);

    [Fact]
    public async Task Missing_host_fails_without_touching_docker()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--host", console.ErrorText);
    }

    [Fact]
    public async Task Missing_dockerfile_points_at_rask_new_docker()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        // No Dockerfile seeded.
        var command = new DeployCommand(console, new FakeFileSystem(), runner, WorkingDir);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--docker", console.ErrorText);
    }

    [Fact]
    public async Task Dry_run_prints_commands_and_runs_nothing()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "shop.example.com", "--name", "shop", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("docker -H ssh://deploy@box build -t shop:latest", console.OutText);
        Assert.Contains("caddy reload", console.OutText);
    }

    [Fact]
    public async Task Port_mode_publishes_the_port_and_persists_config()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => !i.Captured && i.Arguments.Contains("-p") && i.Arguments.Contains("9000:8080"));
        var config = DeployConfig.Load(fs, WorkingDir);
        Assert.Equal("deploy@box", config.Host);
        Assert.Equal(9000, config.Port);
    }

    [Fact]
    public async Task Port_mode_persists_env_file_and_project_for_the_next_deploy()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/src/Shop/Dockerfile", "FROM scratch"); // --project points here
        fs.Seed("/proj/.env.prod", "DB=postgres\n");
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--project", "src/Shop", "--env-file", "/proj/.env.prod"], CancellationToken.None);

        Assert.Equal(0, exit);
        var config = DeployConfig.Load(fs, WorkingDir);
        Assert.Equal("/proj/.env.prod", config.EnvFile); // remembered, not dropped
        Assert.Equal("src/Shop", config.Project);
    }

    [Fact]
    public async Task Dry_run_hides_env_file_secret_values()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.env.prod", "DB_PASSWORD=s3cr3t\n");
        var console = new StringConsole();
        var command = Create(fs, new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--env-file", "/proj/.env.prod", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("s3cr3t", console.OutText);         // the secret value is never printed
        Assert.Contains("DB_PASSWORD=…", console.OutText);        // the key is shown, redacted
    }

    [Fact]
    public async Task Port_with_domain_is_rejected()
    {
        var console = new StringConsole();
        var runner = new FakeProcessRunner();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "app.example.com", "--port", "9000"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("--port doesn't apply with --domain", console.ErrorText);
    }

    [Fact]
    public async Task Port_with_a_remembered_domain_is_rejected_with_guidance()
    {
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Name = "shop", Domain = "app.example.com" }.Save(fs, WorkingDir);
        var console = new StringConsole();
        var command = Create(fs, new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--port", "9000"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(".rask/deploy.json", console.ErrorText);
    }

    [Fact]
    public async Task Blue_green_frees_the_target_color_name_before_starting_it()
    {
        var fs = new FakeFileSystem();
        // A prior deploy left BOTH colors behind (e.g. a failed reload); current is green → new is blue.
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-green\tdemo\tdemo.example.com\tgreen\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        int FreeNew = runs.FindIndex(i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-blue"]);
        int StartNew = runs.FindIndex(i => i.Arguments.Contains("run") && i.Arguments.Contains("demo-blue"));
        Assert.True(FreeNew >= 0 && StartNew >= 0 && FreeNew < StartNew, "the target-color name is removed before it is started");
    }

    [Fact]
    public async Task Env_flags_and_env_file_become_docker_e_arguments()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.env.prod", "# comment\nDB=postgres\n\nTOKEN=abc\n");
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--env-file", "/proj/.env.prod", "--env", "EXTRA=1"], CancellationToken.None);

        Assert.Equal(0, exit);
        var run = runner.Invocations.First(i => !i.Captured && i.Arguments.Contains("run"));
        Assert.Contains("DB=postgres", run.Arguments);
        Assert.Contains("TOKEN=abc", run.Arguments);
        Assert.Contains("EXTRA=1", run.Arguments);
    }

    [Fact]
    public async Task Bad_env_is_rejected()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--env", "NOEQUALS"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("KEY=VALUE", console.ErrorText);
    }

    [Fact]
    public async Task Domain_deploy_writes_a_caddyfile_covering_every_live_app()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var caddyfile = fs.Files.First(f => f.Key.EndsWith("rask-demo.Caddyfile", StringComparison.Ordinal)).Value;
        Assert.Contains("demo.example.com {", caddyfile);       // the new app
        Assert.Contains("reverse_proxy demo-blue:8080", caddyfile);
        Assert.Contains("shop.example.com {", caddyfile);       // the existing app is preserved
        Assert.Contains("reverse_proxy shop-blue:8080", caddyfile);
    }

    [Fact]
    public async Task Blue_green_starts_new_reloads_then_removes_old_in_order()
    {
        var fs = new FakeFileSystem();
        // demo is already deployed on blue → the redeploy must go to green.
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        int StartNew = runs.FindIndex(i => i.Arguments.Contains("run") && i.Arguments.Contains("demo-green"));
        int Reload = runs.FindIndex(i => i.Arguments.Contains("reload"));
        int RemoveOld = runs.FindIndex(i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-blue"]);

        Assert.True(StartNew >= 0 && Reload >= 0 && RemoveOld >= 0);
        Assert.True(StartNew < Reload, "new container must start before Caddy is reloaded");
        Assert.True(Reload < RemoveOld, "the old container must be removed only after the switch");
    }

    [Fact]
    public async Task New_container_that_never_comes_up_leaves_the_old_one_serving()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner
        {
            // ps returns the existing blue; inspect reports the new container never became Running.
            CaptureHandler = args =>
                IsHostProbe(args) ? new ProcessResult(0, ReadyHostProbe, string.Empty)
                : args.Contains("ps") ? new ProcessResult(0, "demo-blue\tdemo\tdemo.example.com\tblue\n", string.Empty)
                : args.Contains("inspect") ? new ProcessResult(0, "false\n", string.Empty)
                : new ProcessResult(0, string.Empty, string.Empty),
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        Assert.Contains(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]); // failed new removed
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("reload"));                                 // never switched traffic
        Assert.DoesNotContain(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-blue"]); // old kept
    }

    [Fact]
    public async Task Config_is_reused_on_a_bare_redeploy()
    {
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Name = "shop", Port = 9000 }.Save(fs, WorkingDir);
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("ssh://deploy@box") && i.Arguments.Contains("build"));
        Assert.Contains(runner.Invocations, i => !i.Captured && i.Arguments.Contains("9000:8080"));
    }

    // ── HTTP health check ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildHealthCheckArguments_probes_over_the_container_network_namespace()
    {
        var args = DeployCommand.BuildHealthCheckArguments("deploy@box", "shop-green", "/health");

        Assert.Equal(
        [
            "-H", "ssh://deploy@box", "run", "--rm", "--network", "container:shop-green",
            "curlimages/curl:8.11.1", "-fsS", "-m", "5", "http://localhost:8080/health",
        ], args);
    }

    [Fact]
    public void BuildHealthCheckArguments_uses_the_custom_path()
    {
        var args = DeployCommand.BuildHealthCheckArguments("deploy@box", "shop", "/ready");

        Assert.Contains("http://localhost:8080/ready", args);
    }

    [Fact]
    public async Task Blue_green_probes_http_health_before_switching_traffic()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        int Probe = runs.FindIndex(i => i.Arguments.Contains("curlimages/curl:8.11.1"));
        int Reload = runs.FindIndex(i => i.Arguments.Contains("reload"));
        Assert.True(Probe >= 0, "the app is probed over HTTP");
        Assert.True(Reload >= 0 && Probe < Reload, "readiness is confirmed before Caddy is reloaded");
        Assert.Contains("container:demo-green", runs[Probe].Arguments); // the NEW color is probed
    }

    [Fact]
    public async Task Failed_health_check_removes_the_new_container_and_keeps_the_old_serving()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"), // running=true
            RunHandler = args => args.Contains("curlimages/curl:8.11.1") ? 1 : 0,   // the probe fails
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        Assert.Contains(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]);       // new removed
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("reload"));                                     // never switched
        Assert.DoesNotContain(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-blue"]); // old kept
        Assert.Contains("health check", console.ErrorText);
        Assert.Contains("--no-health-check", console.ErrorText);
    }

    [Fact]
    public async Task No_health_check_skips_the_probe_and_is_remembered()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo", "--no-health-check"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("curlimages/curl:8.11.1"));
        Assert.True(DeployConfig.Load(fs, WorkingDir).HealthCheckDisabled);
    }

    [Fact]
    public async Task Custom_health_path_reaches_the_probe_and_is_remembered()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--name", "demo", "--health-path", "/ready"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("http://localhost:8080/ready"));
        Assert.Equal("/ready", DeployConfig.Load(fs, WorkingDir).HealthPath);
    }

    [Fact]
    public async Task Health_path_with_no_health_check_is_rejected()
    {
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--no-health-check", "--health-path", "/ready"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--health-path doesn't apply", console.ErrorText);
    }

    [Fact]
    public async Task Dry_run_shows_the_health_probe_and_omits_it_when_disabled()
    {
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);
        await command.ExecuteAsync(["--host", "deploy@box", "--domain", "shop.example.com", "--name", "shop", "--dry-run"], CancellationToken.None);
        Assert.Contains("curlimages/curl:8.11.1", console.OutText);

        var offConsole = new StringConsole();
        var offCommand = Create(new FakeFileSystem(), new FakeProcessRunner(), offConsole);
        await offCommand.ExecuteAsync(["--host", "deploy@box", "--domain", "shop.example.com", "--name", "shop", "--no-health-check", "--dry-run"], CancellationToken.None);
        Assert.DoesNotContain("curlimages/curl", offConsole.OutText);
    }

    [Fact]
    public async Task Port_mode_failed_health_check_reports_and_dumps_logs()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Captures(),                                            // ready host, container running
            RunHandler = args => args.Contains("curlimages/curl:8.11.1") ? 1 : 0,   // the probe fails
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("logs"));
        Assert.Contains("health check", console.ErrorText);
    }

    // ── Host setup (see HostSetupTests for the bootstrap flow itself) ───────────────────────────────

    /// <summary>A bare VPS: root over SSH and nothing else.</summary>
    private const string BareHostProbe = """
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

    [Fact]
    public async Task Setting_up_a_bare_box_remembers_the_new_login_not_the_one_we_were_given()
    {
        // The deploy user replaces root, and root SSH is then disabled — so persisting "root@box"
        // would break every later deploy.
        var fs = new FakeFileSystem();
        var console = new StringConsole { InputLines = ["y"] };
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
                IsHostProbe(args) ? new ProcessResult(0, BareHostProbe, string.Empty)
                : args.Contains("inspect") ? new ProcessResult(0, "true\n", string.Empty)
                : IsHostVerify(args) ? new ProcessResult(0, "rask-ok\n", string.Empty)
                : new ProcessResult(0, string.Empty, string.Empty),
        };
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "root@box", "--name", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("\"host\": \"deploy@box\"", fs.ReadAllText("/proj/.rask/deploy.json"), StringComparison.Ordinal);
        // And the deploy itself must have gone to the new login too.
        Assert.Contains(runner.Invocations, i => i.FileName == "docker" && i.Arguments.Contains("ssh://deploy@box"));
        Assert.DoesNotContain(runner.Invocations, i => i.FileName == "docker" && i.Arguments.Contains("ssh://root@box"));
    }

    [Fact]
    public async Task The_new_login_is_remembered_even_when_the_build_then_fails()
    {
        // Host setup is irreversible from here: root SSH is now off, so `--host root@box` will never
        // work again. If the build fails (a broken Dockerfile — the likeliest first-deploy outcome) and
        // we hadn't persisted, `rask deploy` would say "no host" and `--host root@box` would be
        // refused by the box: locked out by a tool that forgot what it did.
        var fs = new FakeFileSystem();
        var console = new StringConsole { InputLines = ["y"] };
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
                IsHostProbe(args) ? new ProcessResult(0, BareHostProbe, string.Empty)
                : IsHostVerify(args) ? new ProcessResult(0, "rask-ok\n", string.Empty)
                : new ProcessResult(0, string.Empty, string.Empty),
            RunHandler = args => args.Contains("build") ? 1 : 0, // the Docker build fails
        };
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "root@box", "--name", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("\"host\": \"deploy@box\"", fs.ReadAllText("/proj/.rask/deploy.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_bare_box_without_a_terminal_fails_before_docker_is_touched()
    {
        var console = new StringConsole(); // piped: nobody to confirm with
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => IsHostProbe(args) ? new ProcessResult(0, BareHostProbe, string.Empty) : new ProcessResult(0, string.Empty, string.Empty),
        };
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "root@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("--setup-host", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.FileName == "docker" && i.Arguments.Contains("build"));
    }

    [Fact]
    public async Task A_host_that_would_be_read_as_an_ssh_option_is_refused()
    {
        // ssh can't tell a destination from an option, so "-oProxyCommand=…" as a host runs that
        // command on THIS machine — and the host comes from the *committed* .rask/deploy.json, so a
        // hostile value could arrive by pull request and own anyone who deploys (or the CI runner).
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"-oProxyCommand=touch /tmp/pwned","name":"shop","port":9000}""");
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("would be read as an ssh option", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations); // nothing was launched at all
    }

    [Fact]
    public async Task The_live_url_names_the_machine_not_the_ssh_port()
    {
        // "http://box:2222:9000" is not a URL. The SSH port has nothing to do with the app's.
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box:2222", "--name", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("http://box:9000", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("box:2222:9000", console.OutText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "--setup-host", "--no-setup-host" }, "contradict")]
    [InlineData(new[] { "--no-deploy-user", "--deploy-user", "svc" }, "doesn't apply")]
    [InlineData(new[] { "--deploy-user", "root; rm -rf /" }, "isn't a valid Linux user name")]
    [InlineData(new[] { "--deploy-user", "1bad" }, "isn't a valid Linux user name")]
    public async Task Contradictory_or_unusable_setup_flags_are_rejected_before_we_connect(string[] flags, string expected)
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync([.. new[] { "--host", "root@box", "--name", "shop" }, .. flags], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains(expected, console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations); // nothing reached the network
    }

    [Fact]
    public void Setup_flags_default_to_preparing_the_box_fully()
    {
        var parsed = Schema().Parse(["--host", "root@box"]);

        Assert.True(DeployCommand.TryResolveSetup(parsed, out var mode, out var options, out _));
        Assert.Equal(SetupMode.Ask, mode);
        Assert.Equal("deploy", options.DeployUser);
        Assert.True(options.Firewall);
        Assert.True(options.HardenSsh);
    }

    [Fact]
    public void Each_setup_step_can_be_opted_out_of_individually()
    {
        var parsed = Schema().Parse(["--no-deploy-user", "--no-firewall", "--no-harden-ssh", "--setup-host"]);

        Assert.True(DeployCommand.TryResolveSetup(parsed, out var mode, out var options, out _));
        Assert.Equal(SetupMode.Forced, mode);
        Assert.Null(options.DeployUser);
        Assert.False(options.Firewall);
        Assert.False(options.HardenSsh);
    }

    // ── GitHub Actions ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Github_actions_writes_a_workflow_and_names_the_secrets_to_set()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box.example.com", "--name", "shop", "--github-actions"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(runner.Invocations); // pure scaffolding — works offline, before the box exists
        var workflow = fs.ReadAllText("/proj/.github/workflows/deploy.yml");
        Assert.Contains("rask deploy --no-setup-host", workflow, StringComparison.Ordinal);
        Assert.Contains("cancel-in-progress: false", workflow, StringComparison.Ordinal);
        Assert.Contains("gh secret set RASK_SSH_PRIVATE_KEY", console.OutText, StringComparison.Ordinal);
        Assert.Contains("ssh-keyscan box.example.com", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Github_actions_writes_the_config_the_workflow_will_read()
    {
        // The workflow resolves host/domain from .rask/deploy.json. Generating a workflow before the
        // first successful deploy — the obvious order to work in — would otherwise emit a job that
        // fails on its first run with "No host to deploy to".
        var fs = new FakeFileSystem();
        var command = Create(fs, new FakeProcessRunner(), new StringConsole());

        var exit = await command.ExecuteAsync(
            ["--host", "deploy@box.example.com", "--domain", "shop.example.com", "--name", "shop", "--github-actions"], CancellationToken.None);

        Assert.Equal(0, exit);
        var config = fs.ReadAllText("/proj/.rask/deploy.json");
        Assert.Contains("\"host\": \"deploy@box.example.com\"", config, StringComparison.Ordinal);
        Assert.Contains("\"domain\": \"shop.example.com\"", config, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Github_actions_dry_run_writes_no_config_either()
    {
        var fs = new FakeFileSystem();
        var command = Create(fs, new FakeProcessRunner(), new StringConsole());

        await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--github-actions", "--dry-run"], CancellationToken.None);

        Assert.False(fs.FileExists("/proj/.rask/deploy.json"));
    }

    [Fact]
    public async Task The_keyscan_hint_passes_a_custom_ssh_port_as_a_flag_not_part_of_the_host()
    {
        // `ssh-keyscan box:2222` scans nothing, so RASK_SSH_KNOWN_HOSTS would be set to an empty
        // string and every CI deploy would fail host-key verification.
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        await command.ExecuteAsync(["--host", "deploy@box.example.com:2222", "--name", "shop", "--github-actions"], CancellationToken.None);

        Assert.Contains("ssh-keyscan -p 2222 box.example.com", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("box.example.com:2222 2>", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Github_actions_never_provisions_the_host_from_ci()
    {
        // A box that isn't ready must fail the job, not be silently reconfigured from a runner.
        var fs = new FakeFileSystem();
        var command = Create(fs, new FakeProcessRunner(), new StringConsole());

        await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--github-actions"], CancellationToken.None);

        // Every line the runner actually executes must opt out of host setup. Comments may still
        // mention `rask deploy --setup-host` — that's the instruction to run it from your own machine.
        var executable = fs.ReadAllText("/proj/.github/workflows/deploy.yml")
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => !l.StartsWith('#') && l.Contains("rask deploy", StringComparison.Ordinal))
            .ToList();

        Assert.NotEmpty(executable);
        Assert.All(executable, line => Assert.Contains("--no-setup-host", line, StringComparison.Ordinal));
    }

    [Fact]
    public async Task Github_actions_dry_run_prints_the_workflow_without_writing_it()
    {
        var fs = new FakeFileSystem();
        var console = new StringConsole();
        var command = Create(fs, new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--github-actions", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.False(fs.FileExists("/proj/.github/workflows/deploy.yml"));
        Assert.Contains("name: Deploy", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Github_actions_wont_overwrite_a_workflow_youve_edited()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.github/workflows/deploy.yml", "# mine, hand-tuned");
        var console = new StringConsole();
        var command = Create(fs, new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--name", "shop", "--github-actions"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("# mine, hand-tuned", fs.ReadAllText("/proj/.github/workflows/deploy.yml"));
        Assert.Contains("already exists", console.ErrorText, StringComparison.Ordinal);
    }
}

public sealed class ArgumentSchemaMultiOptionTests
{
    [Fact]
    public void MultiOption_collects_every_repeat_in_order()
    {
        var parsed = new ArgumentSchema().MultiOption("env", 'e')
            .Parse(["--env", "A=1", "-e", "B=2", "--env=C=3"]);

        Assert.False(parsed.HasErrors);
        Assert.Equal(["A=1", "B=2", "C=3"], parsed.MultiOption("env"));
    }

    [Fact]
    public void MultiOption_is_empty_when_absent()
    {
        var parsed = new ArgumentSchema().MultiOption("env").Parse([]);

        Assert.Empty(parsed.MultiOption("env"));
    }
}
