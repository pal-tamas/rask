using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// No step of a deploy waits forever: a build that goes quiet or runs too long is stopped, and any other docker call
/// that does not answer within its deadline ends the deploy, named — with the previous version still serving and the
/// app's lock released, never renewed for a deploy that is no longer running.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string AptStall = "#19 238.0 Get:34 http://archive.ubuntu.com/ubuntu noble-updates/main amd64 libllvm20 amd64 [30.6 MB]";

    private static readonly DeployDeadlines QuickDeadlines =
        DeployDeadlines.Default with { Step = TimeSpan.FromSeconds(30), Build = TimeSpan.FromMinutes(1), BuildIdle = TimeSpan.FromMilliseconds(300), Progress = TimeSpan.FromMilliseconds(25) };

    [Fact]
    public async Task A_build_that_goes_quiet_is_stopped_and_the_previous_version_keeps_serving()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue").WithProxyFile(CaddySites.FragmentPath("demo"), DemoBlueFile).WithProxy(CaddySites.BaseCaddyfile);
        var (demo, stalling, _, console, _) = StallingOn(box, args => args.Contains("build"), null, "#19 167.2 Need to get 51.8 MB of archives.", AptStall);

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exit);
        Assert.True(stalling.Cancelled, "the stalled build is cancelled, which kills the client and its ssh session");
        Assert.Contains("still building", console.OutText, StringComparison.Ordinal);
        Assert.Contains("The build printed nothing for", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(AptStall, console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("\"buildIdleTimeout\"", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
        Assert.Equal(["demo-blue", "rask-caddy"], box.ContainerNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_build_that_runs_past_its_deadline_is_stopped()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue").WithProxyFile(CaddySites.FragmentPath("demo"), DemoBlueFile).WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _, _, console, _) = StallingOn(box, args => args.Contains("build"));
        demo.Deadlines = QuickDeadlines with { Build = TimeSpan.FromMilliseconds(200), BuildIdle = TimeSpan.FromMinutes(1) };

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exit);
        Assert.Contains("The build ran past", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("\"buildTimeout\"", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
    }

    [Fact]
    public async Task A_docker_call_that_never_answers_fails_named_and_takes_the_new_colour_back_out()
    {
        var box = new FakeDockerHost().WithApp("demo", "demo.example.com", "blue").WithProxyFile(CaddySites.FragmentPath("demo"), DemoBlueFile).WithProxy(CaddySites.BaseCaddyfile);
        var (demo, stalling, _, console, _) = StallingOn(box, args => args.Contains("inspect") && args[^1] == "demo-green");
        demo.Deadlines = QuickDeadlines with { Step = TimeSpan.FromMilliseconds(300) };

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(1, exit);
        Assert.True(stalling.Cancelled);
        Assert.Contains("`docker inspect` did not finish within", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("the previous version keeps serving", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
        Assert.Equal(["demo-blue", "rask-caddy"], box.ContainerNames.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_deploy_stopped_at_a_deadline_releases_its_lock_and_stops_renewing_it()
    {
        var box = new FakeDockerHost().WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _, inner, _, _) = StallingOn(box, args => args.Contains("build"));
        demo.AppLockHeartbeat = TimeSpan.FromMilliseconds(5);
        var beats = 0;
        inner.Before = args =>
        {
            if (args.Contains("touch"))
            {
                Interlocked.Increment(ref beats);
            }

            return Task.CompletedTask;
        };

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);
        var afterRelease = Volatile.Read(ref beats);
        await Task.Delay(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);

        Assert.Equal(1, exit);
        Assert.True(afterRelease > 0, "the lease is renewed while the build runs");
        Assert.Equal(afterRelease, Volatile.Read(ref beats));
        Assert.DoesNotContain(box.ContainerNames, name => name.StartsWith(ProxyCommands.LockContainer, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_renewal_that_never_answers_is_a_warning_and_the_deploy_goes_on()
    {
        var box = new FakeDockerHost().WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _, inner, console, _) = StallingOn(box, args => args.Contains("touch"));
        demo.AppLockHeartbeat = TimeSpan.FromMilliseconds(5);
        demo.Deadlines = QuickDeadlines with { Step = TimeSpan.FromMilliseconds(50), BuildIdle = TimeSpan.FromMinutes(1) };
        inner.Before = async args =>
        {
            if (args.Contains("build"))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(300)).ConfigureAwait(false);
            }
        };

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Contains("Renewing the deploy lock rask-deploy-lock-demo failed (`docker exec` did not finish within", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal("demo-blue:8080", box.Routes["demo.example.com"]);
    }

    [Fact]
    public async Task The_build_deadlines_are_read_from_the_deploy_config_and_kept()
    {
        var box = new FakeDockerHost().WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _, _, _, fs) = StallingOn(box, _ => false, config: c =>
        {
            c.BuildTimeout = "1h30m";
            c.BuildIdleTimeout = "25m";
        });
        demo.Deadlines = DeployDeadlines.Default;

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(0, exit);
        Assert.Equal(TimeSpan.FromMinutes(90), demo.Deadlines.Build);
        Assert.Equal(TimeSpan.FromMinutes(25), demo.Deadlines.BuildIdle);
        var saved = DeployConfig.Load(fs, WorkingDir);
        Assert.Equal("1h30m", saved.BuildTimeout);
        Assert.Equal("25m", saved.BuildIdleTimeout);
    }

    [Fact]
    public async Task A_build_deadline_that_is_not_a_duration_fails_before_anything_runs()
    {
        var box = new FakeDockerHost().WithProxy(CaddySites.BaseCaddyfile);
        var (demo, _, inner, console, _) = StallingOn(box, _ => false, config: c => c.BuildIdleTimeout = "forever");
        var calls = 0;
        inner.Before = _ =>
        {
            Interlocked.Increment(ref calls);
            return Task.CompletedTask;
        };

        var exit = await demo.ExecuteAsync([], TestContext.Current.CancellationToken);

        Assert.Equal(2, exit);
        Assert.Contains("\"buildIdleTimeout\": \"forever\" isn't a duration", console.ErrorText, StringComparison.Ordinal);
        Assert.Equal(0, calls);
    }

    [Theory]
    [InlineData(new[] { "-H", "ssh://deploy@box", "build", "-t", "demo:current", "." }, 45 * 60)]
    [InlineData(new[] { "-H", "ssh://deploy@box", "stop", "-t", "1800", "demo-blue" }, (5 * 60) + 1800)]
    [InlineData(new[] { "-H", "ssh://deploy@box", "exec", "rask-caddy", "caddy", "reload" }, 5 * 60)]
    [InlineData(new[] { "-H", "ssh://deploy@box", "logs", "--tail", "50", "demo-blue" }, 5 * 60)]
    [InlineData(new[] { "--version" }, 5 * 60)]
    public void Each_docker_call_gets_the_deadline_of_its_step(string[] arguments, int seconds)
    {
        var deadline = DeployDeadlines.Default.For(arguments);

        Assert.Equal(TimeSpan.FromSeconds(seconds), deadline);
    }

    [Fact]
    public void Following_the_logs_has_no_deadline()
    {
        var deadline = DeployDeadlines.Default.For(["-H", "ssh://deploy@box", "logs", "--tail", "100", "--follow", "demo-blue"]);

        Assert.Null(deadline);
    }

    private static (DeployCommand Command, StallingRunner Stalling, FakeDockerHost.Runner Inner, StringConsole Console, FakeFileSystem Fs) StallingOn(
        FakeDockerHost box, Func<IReadOnlyList<string>, bool> stalls, Action<DeployConfig>? config = null, params string[] lines)
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        var settings = new DeployConfig { Host = "deploy@box", Name = "demo", Domains = ["demo.example.com"] };
        config?.Invoke(settings);
        settings.Save(fs, WorkingDir);
        var inner = box.For(fs);
        var stalling = new StallingRunner(inner, stalls, lines);
        var console = new StringConsole();
        var command = new DeployCommand(console, fs, stalling, WorkingDir)
        {
            ReadinessDelay = TimeSpan.Zero,
            ReadinessAttempts = 1,
            PreStopDrainDelay = TimeSpan.Zero,
            LockPollDelay = TimeSpan.Zero,
            Deadlines = QuickDeadlines,
        };
        return (command, stalling, inner, console, fs);
    }
}
