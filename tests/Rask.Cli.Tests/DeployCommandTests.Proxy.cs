using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The shared proxy, one file per app: two deploys at once cannot overwrite each other's routes, every write is
/// checked, the switch happens under the box's deploy lock, and a route is checked through Caddy before the old
/// colour goes. The 2026-10-20 incident — two apps deployed in the same minute, one left routed to a removed
/// colour while both reported success — is replayed step for step against a box with state.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string OldSingleFile =
        "eszaf.hu {\n\tencode zstd gzip\n\treverse_proxy eszaf-green:8080\n}\n\nppta.hu {\n\tencode zstd gzip\n\treverse_proxy ta-blue:8080\n}\n";

    // A Caddy admin API reply for one site, in the shape `caddy adapt` gives the Caddyfile Rask writes.
    private const string AdaptedConfig = """
        {"apps":{"http":{"servers":{"srv0":{"listen":[":443"],"routes":[
          {"match":[{"host":["demo.example.com","www.demo.example.com"]}],"handle":[{"handler":"subroute","routes":[{"handle":[
            {"encodings":{"gzip":{},"zstd":{}},"handler":"encode","prefer":["zstd","gzip"]},
            {"handler":"reverse_proxy","upstreams":[{"dial":"demo-green:8080"}]}]}]}],"terminal":true},
          {"match":[{"host":["shop.example.com"]}],"handle":[{"handler":"subroute","routes":[{"handle":[
            {"handler":"reverse_proxy","upstreams":[{"dial":"shop-blue:3000"}]}]}]}],"terminal":true}]}}}}}
        """;

    [Fact]
    public async Task Two_apps_deployed_in_the_incident_order_each_end_routed_to_their_new_colour()
    {
        var box = new FakeDockerHost().WithApp("ta", "ppta.hu", "blue").WithApp("eszaf", "eszaf.hu", "green").WithProxy(OldSingleFile);
        var (ta, taRunner) = CommandOn(box, "ta", "ppta.hu");
        var (eszaf, _) = CommandOn(box, "eszaf", "eszaf.hu");
        var taGated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var eszafDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        taRunner.Before = async args =>
        {
            // TA has listed the box (eszaf-green live), started ta-green and is health-gating it: hold it there.
            if (args.Contains("container:ta-green") && args.Contains("http://localhost:8080/health"))
            {
                taGated.TrySetResult();
                await eszafDone.Task.ConfigureAwait(false);
            }
        };

        var taDeploy = ta.ExecuteAsync([], CancellationToken.None);
        await taGated.Task;
        var eszafExit = await eszaf.ExecuteAsync([], CancellationToken.None); // blue goes live, green is removed
        eszafDone.SetResult();
        var taExit = await taDeploy;

        Assert.Equal(0, eszafExit);
        Assert.Equal(0, taExit);
        Assert.Equal("eszaf-blue:8080", box.Routes["eszaf.hu"]);
        Assert.Equal("ppta.hu", Assert.Single(box.Routes, route => route.Value == "ta-green:8080").Key);
        Assert.All(box.Routes.Values, upstream => Assert.Contains(upstream!.Split(':')[0], box.ContainerNames));
        Assert.Equal(["eszaf-blue", "rask-caddy", "ta-green"], box.ContainerNames.Order(StringComparer.Ordinal));
        Assert.Contains("reverse_proxy eszaf-blue:8080", box.ProxyFile("/data/rask/sites/eszaf.caddy"), StringComparison.Ordinal);
        Assert.Contains("reverse_proxy ta-green:8080", box.ProxyFile("/data/rask/sites/ta.caddy"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_single_file_box_moves_to_one_file_per_app_keeping_each_app_on_what_it_serves()
    {
        // shop runs two colours (a deploy an older Rask left half done): the old file says which one serves.
        const string single = "shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n";
        var box = new FakeDockerHost().WithApp("shop", "shop.example.com", "blue").WithApp("shop", "shop.example.com", "green").WithProxy(single);
        var (demo, _) = CommandOn(box, "demo", "demo.example.com");

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(CaddySites.BaseCaddyfile, box.ProxyFile(CaddySites.BasePath));
        Assert.Equal("shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n", box.ProxyFile("/data/rask/sites/shop.caddy"));
        Assert.Equal("shop-blue:8080", box.Routes["shop.example.com"]);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
    }

    [Fact]
    public async Task Moving_to_one_file_per_app_twice_changes_nothing_the_second_time()
    {
        var box = new FakeDockerHost().WithApp("shop", "shop.example.com", "blue")
            .WithProxy("shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n");
        var (demo, runner) = CommandOn(box, "demo", "demo.example.com");
        Assert.Equal(0, await demo.ExecuteAsync([], CancellationToken.None));
        var writes = new List<string>();
        runner.Before = args =>
        {
            if (args.Contains("cp"))
            {
                writes.Add(args[^1]);
            }

            return Task.CompletedTask;
        };

        var again = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, again);
        Assert.StartsWith("rask-caddy:/data/rask/sites/demo.caddy.tmp-", Assert.Single(writes), StringComparison.Ordinal);
        Assert.Equal("demo-green:8080", box.Routes["demo.example.com"]);
        Assert.Equal("shop-blue:8080", box.Routes["shop.example.com"]);
    }

    [Fact]
    public async Task A_proxy_file_that_cannot_be_copied_keeps_the_old_colour_serving()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue")
            .WithProxyFile("/data/rask/sites/demo.caddy", "demo.example.com {\n\tencode zstd gzip\n\treverse_proxy demo-blue:8080\n}\n").WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _) = CommandOn(box, "demo", "demo.example.com", out var console);
        box.CopyFails = true;

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
        Assert.Contains("demo-blue", box.ContainerNames);
        Assert.DoesNotContain("demo-green", box.ContainerNames);
        Assert.Contains("Writing /data/rask/sites/demo.caddy in the proxy failed", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("Deployed.", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_failed_copy_reloads_nothing_and_retires_nothing()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner
        {
            CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")),
            RunHandler = args => args.Contains("cp") ? 1 : 0,
        };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("mv"));
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("reload"));
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("stop"));
        Assert.Contains(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]);
        Assert.DoesNotContain(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-blue"]);
    }

    [Fact]
    public async Task A_route_the_proxy_does_not_serve_is_put_back_and_the_new_colour_removed()
    {
        const string previous = "demo.example.com {\n\tencode zstd gzip\n\treverse_proxy demo-blue:8080\n}\n";
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(fs, runner, console);
        var listing = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        runner.CaptureHandler = args =>
            args.SequenceEqual(ProxyCommands.BuildProxyReadArguments("deploy@box", "/data/rask/sites/demo.caddy")) ? new ProcessResult(0, previous, string.Empty)
            // The reload took, but the proxy still sends the site to the old colour.
            : args.Contains(ProxyCommands.AdminConfigUrl) ? new ProcessResult(0, FakeProxy.AdminConfig([previous]), string.Empty)
            : listing(args);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).ToList();
        Assert.Equal(2, runs.Count(i => i.Arguments.Contains("reload"))); // the switch, then the switch back
        Assert.Equal(previous, FakeProxy.WrittenFiles(fs).Last());
        Assert.Contains(runs, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]);
        Assert.DoesNotContain(runs, i => i.Arguments.Contains("stop"));
        Assert.Contains("The proxy is not serving demo.example.com from demo-green: its running config sends demo.example.com to demo-blue:8080", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("Deployed.", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_route_that_answers_bad_gateway_through_the_proxy_is_put_back()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue")
            .WithProxyFile("/data/rask/sites/demo.caddy", "demo.example.com {\n\tencode zstd gzip\n\treverse_proxy demo-blue:8080\n}\n").WithProxy(CaddySites.BaseCaddyfile);
        var (demo, runner) = CommandOn(box, "demo", "demo.example.com", out var console);
        runner.Before = args =>
        {
            // The new colour dies the moment the proxy is switched to it.
            if (args.Contains("reload") && box.ContainerNames.Contains("demo-green"))
            {
                return box.For(new FakeFileSystem()).RunAsync("docker", ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"], null, CancellationToken.None);
            }

            return Task.CompletedTask;
        };

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
        Assert.Contains("demo-blue", box.ContainerNames);
        Assert.Contains("https://demo.example.com/health answered 502 through it", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_lock_is_taken_after_the_health_gate_and_held_until_the_route_is_checked()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        int Lock = all.FindIndex(i => i.Arguments.Contains(ProxyCommands.LockContainer));
        int Unlock = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", ProxyCommands.LockContainer]);
        int Gate = all.FindIndex(i => i.Arguments.Contains("container:demo-green"));
        int Reload = all.FindIndex(i => i.Arguments.Contains("reload"));
        int Relist = all.FindLastIndex(Reload, i => i.Arguments.Contains("ps"));
        int Check = all.FindIndex(i => i.Arguments.Contains("--resolve"));
        int Retire = all.FindIndex(i => i.Arguments.SequenceEqual(DockerCommands.BuildStopArguments("deploy@box", "demo-blue", ShutdownBudget.DefaultStopTimeout)));
        Assert.True(Gate < Lock, "the health gate runs before the lock is taken");
        Assert.True(Lock < Relist && Relist < Reload, "the containers are listed again under the lock");
        Assert.True(Reload < Check && Check < Unlock, "reload and check happen under the lock");
        Assert.True(Unlock < Retire, "the old colour drains after the box's lock is let go");
    }

    [Fact]
    public async Task A_deploy_waits_while_another_holds_the_lock_and_names_it()
    {
        var attempts = 0;
        var fs = new FakeFileSystem();
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
                args.Contains("sleep") && ++attempts <= 2 ? new ProcessResult(125, string.Empty, "Conflict. The container name \"/rask-deploy-lock\" is already in use")
                : args.Contains("{{index .Config.Labels \"rask.lock\"}}") ? new ProcessResult(0, "eszaf from ci-runner at 2026-10-20 09:14:03Z\n", string.Empty)
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(3, attempts);
        Assert.Contains("Another deploy is switching the proxy on this box (eszaf from ci-runner at 2026-10-20 09:14:03Z)", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_lock_that_is_never_freed_fails_the_deploy_and_leaves_the_proxy_alone()
    {
        var fs = new FakeFileSystem();
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("sleep")
                ? new ProcessResult(125, string.Empty, "Conflict. The container name \"/rask-deploy-lock\" is already in use")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);
        command.LockAttempts = 3;

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("reload") || i.Arguments.Contains("cp"));
        Assert.Contains(runner.Invocations, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]);
        Assert.Contains("rm -f rask-deploy-lock", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_box_without_the_network_creates_it_under_the_lock_before_starting_anything()
    {
        var box = new FakeDockerHost { HasNetwork = false }.WithProxy(CaddySites.BaseCaddyfile);
        var (demo, runner) = CommandOn(box, "demo", "demo.example.com");
        var order = new List<string>();
        runner.Before = args =>
        {
            order.Add(args.Contains(ProxyCommands.LockContainer) ? "lock" : args.Contains("create") ? "create" : args.Contains("rask.app=demo") ? "start" : "other");
            return Task.CompletedTask;
        };

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.True(box.HasNetwork);
        var meaningful = order.Where(step => step != "other").ToList();
        Assert.Equal(["lock", "create", "start", "lock"], meaningful);
    }

    [Fact]
    public void Two_containers_of_one_app_route_to_the_one_the_proxy_already_serves()
    {
        // docker ps lists the newest first: green is newer, but blue is what the proxy routes.
        IReadOnlyList<DeployedApp> apps =
        [
            new("shop-green", "shop", "shop.example.com", "green", 8080),
            new("shop-blue", "shop", "shop.example.com", "blue", 8080),
        ];

        var map = CaddyRouting.BuildRoutingMap(apps, "demo", "demo.example.com", new RouteTarget("demo-blue", 8080), new HashSet<string>(StringComparer.Ordinal) { "shop-blue" });

        Assert.Equal("shop-blue", map["shop.example.com"].Container);
    }

    [Fact]
    public void Two_containers_of_one_app_with_nothing_to_tell_them_apart_route_to_the_newest_and_say_so()
    {
        IReadOnlyList<DeployedApp> containers =
        [
            new("shop-green", "shop", "shop.example.com", "green", 8080),
            new("shop-blue", "shop", "shop.example.com", "blue", 8080),
        ];

        var choice = CaddyRouting.ChooseServing(containers, new HashSet<string>(StringComparer.Ordinal));

        Assert.Equal("shop-green", choice.Container.Container);
        Assert.True(choice.Guessed);
        Assert.Equal("shop-green", CaddyRouting.BuildRoutingMap(containers, "demo", "demo.example.com", new RouteTarget("demo-blue", 8080))["shop.example.com"].Container);
    }

    [Fact]
    public void The_upstreams_of_a_site_are_read_from_the_config_caddy_runs()
    {
        var demo = CaddyRouteCheck.Upstreams(AdaptedConfig, "demo.example.com");
        var www = CaddyRouteCheck.Upstreams(AdaptedConfig, "www.demo.example.com");
        var missing = CaddyRouteCheck.Upstreams(AdaptedConfig, "eszaf.hu");
        var garbage = CaddyRouteCheck.Upstreams("<html>502</html>", "demo.example.com");

        Assert.Equal(["demo-green:8080"], demo);
        Assert.Equal(["demo-green:8080"], www);
        Assert.Empty(missing);
        Assert.Empty(garbage);
    }

    [Theory]
    [InlineData("200", true, true)]
    [InlineData("302", true, true)]
    [InlineData("500", true, false)]
    [InlineData("502", true, false)]
    [InlineData("404", false, true)]
    [InlineData("502", false, false)]
    [InlineData("000", false, false)]
    public void A_status_through_the_proxy_counts_as_the_app_answering_only_when_it_is_the_apps(string status, bool healthEnabled, bool answered) =>
        Assert.Equal(answered, CaddyRouteCheck.Answered(status, healthEnabled));

    [Fact]
    public void Only_a_base_caddyfile_that_imports_the_app_files_counts_as_the_new_layout()
    {
        var withOptions = "{\n\temail ops@example.com\n}\n" + CaddySites.BaseCaddyfile;

        Assert.True(CaddySites.IsFragmentLayout(CaddySites.BaseCaddyfile));
        Assert.True(CaddySites.IsFragmentLayout(withOptions));
        Assert.False(CaddySites.IsFragmentLayout(OldSingleFile));
        Assert.False(CaddySites.IsFragmentLayout(FakeDockerHost.StockCaddyfile));
        Assert.False(CaddySites.IsFragmentLayout(null));
    }

    [Fact]
    public void Only_finished_app_files_are_listed_never_a_copy_still_on_its_way_in()
    {
        var apps = CaddySites.AppsWithFragments("eszaf.caddy\nta.caddy\nta.caddy.tmp-0123abcd\n");

        Assert.Equal(["eszaf", "ta"], apps);
    }

    [Fact]
    public void A_wildcard_site_is_checked_under_a_name_of_its_own()
    {
        var probe = CaddyRouteCheck.ProbeHost("*.eszaf.hu");

        Assert.Equal("rask-check.eszaf.hu", probe);
    }

    /// <summary>A deploy of <paramref name="app"/> on <paramref name="domain"/>, remembered as a bare redeploy would find it, against <paramref name="box"/>.</summary>
    private static (DeployCommand Command, FakeDockerHost.Runner Runner) CommandOn(FakeDockerHost box, string app, string domain) =>
        CommandOn(box, app, domain, out _);

    private static (DeployCommand Command, FakeDockerHost.Runner Runner) CommandOn(FakeDockerHost box, string app, string domain, out StringConsole console)
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        new DeployConfig { Host = "deploy@box", Name = app, Domains = [domain] }.Save(fs, WorkingDir);
        var runner = box.For(fs);
        console = new StringConsole();
        var command = new DeployCommand(console, fs, runner, WorkingDir)
        {
            ReadinessDelay = TimeSpan.Zero,
            ReadinessAttempts = 1,
            PreStopDrainDelay = TimeSpan.Zero,
            LockPollDelay = TimeSpan.Zero,
        };
        return (command, runner);
    }
}
