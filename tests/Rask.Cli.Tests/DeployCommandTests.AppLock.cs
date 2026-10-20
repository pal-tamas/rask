using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// Two deploys of one app take turns on the app's own lock, held from before the image tags move until the old colour
/// is retired; deploys of different apps never wait on each other's. And a stopped app's file in the proxy stays:
/// no other app's deploy deletes or edits it, and the names it holds stay taken until someone removes it.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string DemoBlueFile = "demo.example.com {\n\tencode zstd gzip\n\treverse_proxy demo-blue:8080\n}\n";

    [Fact]
    public async Task Two_deploys_of_one_app_take_turns_and_both_land()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue").WithProxyFile(CaddySites.FragmentPath("demo"), DemoBlueFile).WithProxy(CaddySites.BaseCaddyfile);
        var (first, firstRunner) = CommandOn(box, "demo", "demo.example.com");
        var (second, secondRunner) = CommandOn(box, "demo", "demo.example.com", out var secondConsole);
        var firstBuilding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var letFirstGo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        firstRunner.Before = async args =>
        {
            if (args.Contains("build"))
            {
                firstBuilding.TrySetResult();
                await letFirstGo.Task.ConfigureAwait(false);
            }
        };
        Task<int>? firstDeploy = null;
        var secondAttempts = 0;
        secondRunner.Before = async args =>
        {
            // The second deploy found the lock held once; on its next try the first one finishes, then it may go.
            if (args.Contains(ProxyCommands.AppLockContainer("demo")) && args.Contains("run") && ++secondAttempts == 2)
            {
                letFirstGo.SetResult();
                await firstDeploy!.ConfigureAwait(false);
            }
        };

        firstDeploy = first.ExecuteAsync([], CancellationToken.None);
        await firstBuilding.Task;
        var secondExit = await second.ExecuteAsync([], CancellationToken.None);
        var firstExit = await firstDeploy;

        Assert.Equal(0, firstExit);
        Assert.Equal(0, secondExit);
        Assert.Equal(2, secondAttempts);
        Assert.Contains("Another deploy is deploying 'demo' to this box (demo from ", secondConsole.OutText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]); // blue → green, then green → blue
        Assert.Equal(["demo-blue", "rask-caddy"], box.ContainerNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_deploy_of_one_app_never_waits_on_another_apps_deploy()
    {
        var box = new FakeDockerHost().WithApp("shop", "shop.example.com", "blue").WithProxy(CaddySites.BaseCaddyfile);
        var (shop, shopRunner) = CommandOn(box, "shop", "shop.example.com");
        var (demo, _) = CommandOn(box, "demo", "demo.example.com", out var demoConsole);
        var shopBuilding = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var letShopGo = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        shopRunner.Before = async args =>
        {
            if (args.Contains("build"))
            {
                shopBuilding.TrySetResult();
                await letShopGo.Task.ConfigureAwait(false);
            }
        };

        var shopDeploy = shop.ExecuteAsync([], CancellationToken.None);
        await shopBuilding.Task;
        var demoExit = await demo.ExecuteAsync([], CancellationToken.None); // while shop holds its own lock
        letShopGo.SetResult();
        var shopExit = await shopDeploy;

        Assert.Equal(0, demoExit);
        Assert.Equal(0, shopExit);
        Assert.DoesNotContain("waiting for it", demoConsole.OutText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
        Assert.Equal("shop-green:8080", box.Routes["shop.example.com"]);
    }

    [Fact]
    public async Task A_held_app_lock_is_renewed_while_the_deploy_runs_and_no_longer_once_it_is_released()
    {
        var box = new FakeDockerHost().WithProxy(CaddySites.BaseCaddyfile);
        var (demo, runner) = CommandOn(box, "demo", "demo.example.com");
        demo.AppLockHeartbeat = TimeSpan.FromMilliseconds(5);
        var beats = 0;
        runner.Before = async args =>
        {
            if (args.Contains("touch"))
            {
                Interlocked.Increment(ref beats);
            }

            if (args.Contains("build"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false); // a long build
            }
        };

        var exit = await demo.ExecuteAsync([], CancellationToken.None);
        var afterRelease = Volatile.Read(ref beats);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.True(afterRelease > 0, "the lease is renewed during the build");
        Assert.Equal(afterRelease, Volatile.Read(ref beats));
        Assert.DoesNotContain(box.ContainerNames, name => name.StartsWith(ProxyCommands.LockContainer, StringComparison.Ordinal));
    }

    [Fact]
    public void An_app_lock_lives_only_while_its_heartbeat_is_fresh()
    {
        var take = ProxyCommands.BuildAppLockArguments("deploy@box", "demo", "demo from ci", leaseSeconds: 300);
        var renew = ProxyCommands.BuildLockHeartbeatArguments("deploy@box", "id0001");

        Assert.Equal(["-H", "ssh://deploy@box", "run", "-d", "--rm", "--name", "rask-deploy-lock-demo", "--network", "none"], take.Take(9));
        Assert.Equal("touch /rask-lock-beat; while [ $(( $(date +%s) - $(stat -c %Y /rask-lock-beat) )) -lt 300 ]; do sleep 2; done", take[^1]);
        Assert.Equal(["-H", "ssh://deploy@box", "exec", "id0001", "touch", "/rask-lock-beat"], renew);
        Assert.True(ProxyCommands.AppLockLeaseSeconds >= 3 * ProxyCommands.AppLockHeartbeatSeconds, "the lease outlasts a couple of missed beats");
    }

    [Fact]
    public async Task An_app_lock_that_is_never_freed_fails_the_deploy_before_it_builds()
    {
        var fs = new FakeFileSystem();
        var scripted = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("rask-deploy-lock-demo") && args.Contains("run")
                ? new ProcessResult(125, string.Empty, "Conflict. The container name \"/rask-deploy-lock-demo\" is already in use")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);
        command.AppLockAttempts = 3;

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("build") || i.Arguments.Contains("tag"));
        Assert.Contains("rm -f rask-deploy-lock-demo", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_stopped_apps_file_stays_and_its_names_stay_taken()
    {
        const string gone = "gone.example.com {\n\tencode zstd gzip\n\treverse_proxy gone-blue:8080\n}\n";
        var box = new FakeDockerHost().WithProxyFile(CaddySites.FragmentPath("gone"), gone).WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _) = CommandOn(box, "demo", "demo.example.com");
        var (claimer, _) = CommandOn(box, "newapp", "gone.example.com", out var claimerConsole);

        var demoExit = await demo.ExecuteAsync([], CancellationToken.None);
        var claimExit = await claimer.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, demoExit);
        Assert.Equal(gone, box.ProxyFile(CaddySites.FragmentPath("gone")));
        Assert.Equal(1, claimExit);
        Assert.DoesNotContain(box.ContainerNames, name => name.StartsWith("newapp", StringComparison.Ordinal));
        Assert.Contains("gone.example.com is named by the proxy's file for the app 'gone' (/data/rask/sites/gone.caddy)", claimerConsole.ErrorText, StringComparison.Ordinal);
        Assert.Contains("exec rask-caddy rm /data/rask/sites/gone.caddy", claimerConsole.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_an_old_box_never_edits_a_file_an_app_already_has()
    {
        const string shopOwn = "shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-green:8080\n}\n";
        var box = new FakeDockerHost().WithApp("shop", "shop.example.com", "green")
            .WithProxyFile(CaddySites.FragmentPath("shop"), shopOwn)
            .WithProxy("shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n");
        var (demo, _) = CommandOn(box, "demo", "demo.example.com");

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(shopOwn, box.ProxyFile(CaddySites.FragmentPath("shop")));
        Assert.Equal("shop-green:8080", box.Routes["shop.example.com"]);
    }

    [Fact]
    public async Task A_proxy_recreated_by_hand_gets_its_import_back_and_no_other_apps_file_is_written()
    {
        var box = new FakeDockerHost().WithApp("shop", "shop.example.com", "blue")
            .WithProxyFile(CaddySites.FragmentPath("shop"), "shop.example.com {\n\tencode zstd gzip\n\treverse_proxy shop-blue:8080\n}\n")
            .WithProxy(FakeDockerHost.StockCaddyfile);
        var (demo, runner) = CommandOn(box, "demo", "demo.example.com");
        var copies = new List<string>();
        runner.Before = args =>
        {
            if (args.Contains("cp"))
            {
                copies.Add(args[^1]);
            }

            return Task.CompletedTask;
        };

        var exit = await demo.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(copies, copy => copy.Contains("shop.caddy", StringComparison.Ordinal));
        Assert.Equal(CaddySites.BaseCaddyfile, box.ProxyFile(CaddySites.BasePath));
        Assert.Equal("shop-blue:8080", box.Routes["shop.example.com"]);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
    }

    [Fact]
    public void A_name_in_another_apps_file_is_found_with_the_app_that_holds_it()
    {
        var files = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["demo"] = "demo.example.com {\n}\n",
            ["eszaf"] = "eszaf.hu, www.eszaf.hu {\n\treverse_proxy eszaf-blue:8080\n}\n\n*.eszaf.hu {\n\tredir https://eszaf.hu{uri} 308\n}\n",
        };

        var served = CaddySites.FindHostInFiles(files, "ta", "WWW.eszaf.hu", string.Empty);
        var redirected = CaddySites.FindHostInFiles(files, "ta", "ppta.hu", "*.eszaf.hu");
        var own = CaddySites.FindHostInFiles(files, "demo", "demo.example.com", string.Empty);
        var free = CaddySites.FindHostInFiles(files, "ta", "ppta.hu", string.Empty);

        Assert.Equal(("www.eszaf.hu", "eszaf"), served);
        Assert.Equal(("*.eszaf.hu", "eszaf"), redirected);
        Assert.Null(own);
        Assert.Null(free);
    }
}
