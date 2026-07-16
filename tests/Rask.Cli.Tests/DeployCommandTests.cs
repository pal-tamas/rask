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

    /// <summary>A capture handler for the domain flow: ok preflight, a given `docker ps` listing, container up.</summary>
    private static Func<IReadOnlyList<string>, ProcessResult> Captures(string psListing) => args =>
        args.Contains("ps") ? new ProcessResult(0, psListing, string.Empty)
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
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "true\n", string.Empty) };
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
    public async Task Env_flags_and_env_file_become_docker_e_arguments()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.env.prod", "# comment\nDB=postgres\n\nTOKEN=abc\n");
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "true\n", string.Empty) };
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
                args.Contains("ps") ? new ProcessResult(0, "demo-blue\tdemo\tdemo.example.com\tblue\n", string.Empty)
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
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "true\n", string.Empty) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("ssh://deploy@box") && i.Arguments.Contains("build"));
        Assert.Contains(runner.Invocations, i => !i.Captured && i.Arguments.Contains("9000:8080"));
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
