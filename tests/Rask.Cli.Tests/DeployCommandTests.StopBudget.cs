using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// A deploy moves traffic to the new colour at once, then lets the old one finish the jobs it is running, for up to
/// the app's <c>stopTimeout</c>, saying what it waits for — and kills it, loudly, only when that runs out.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string DrainLine =
        "info: Rask.Background.JobProcessor[0]\n      Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m12s)\n";

    [Fact]
    public async Task The_stop_budget_comes_from_the_deploy_config_and_reaches_both_the_stop_and_the_app()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","name":"demo","domain":"demo.example.com","stopTimeout":"45m"}""");
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.SequenceEqual(DockerCommands.BuildStopArguments("deploy@box", "demo-blue", TimeSpan.FromMinutes(45))));
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("run") && i.Arguments.Contains("ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=2695"));
    }

    [Fact]
    public async Task A_first_deploy_writes_the_default_stop_budget_where_it_can_be_changed()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("\"stopTimeout\": \"30m\"", fs.Files[Path.GetFullPath("/proj/.rask/deploy.json")], StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=1795"));
    }

    [Fact]
    public async Task A_stop_budget_that_is_not_a_duration_fails_before_anything_runs()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","name":"demo","stopTimeout":"forever"}""");
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.NotEqual(0, exit);
        Assert.Contains("stopTimeout", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("build"));
    }

    [Fact]
    public async Task While_the_old_colour_finishes_its_jobs_the_deploy_says_what_it_waits_for()
    {
        var fs = new FakeFileSystem();
        var inner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var runner = new SlowStop(inner, "demo-blue", reportsBeforeExit: 2, logs: DrainLine);
        var console = new StringConsole();
        var command = CreateOn(fs, inner, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("demo-blue is finishing 1 job: AtPump (3m12s)", console.OutText, StringComparison.Ordinal);
        Assert.Contains("of 30m", console.OutText, StringComparison.Ordinal);
        Assert.Contains(inner.Invocations, i => i.Arguments is [.., "rm", "-f", "demo-blue"]);
        Assert.DoesNotContain("killed", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_old_colour_still_busy_when_the_budget_ends_is_killed_and_it_is_said_loudly()
    {
        var fs = new FakeFileSystem();
        var scripted = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n");
        var inner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("{{.State.ExitCode}}") ? new ProcessResult(0, "137\n", string.Empty) : scripted(args),
        };
        var runner = new SlowStop(inner, "demo-blue", reportsBeforeExit: 1, logs: DrainLine);
        var console = new StringConsole();
        var command = CreateOn(fs, inner, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("demo-blue was still running when its 30m stop budget ran out and was killed", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("1 job: AtPump (3m12s)", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("\"stopTimeout\"", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_old_colour_is_stopped_after_the_box_lock_is_let_go_and_under_the_apps_own()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        int BoxUnlock = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", ProxyCommands.LockContainer]);
        int Retire = all.FindIndex(i => i.Arguments.SequenceEqual(DockerCommands.BuildStopArguments("deploy@box", "demo-blue", ShutdownBudget.DefaultStopTimeout)));
        int AppUnlock = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", "rask-deploy-lock-demo"]);
        Assert.True(BoxUnlock >= 0 && BoxUnlock < Retire, "other apps' deploys do not wait out this app's drain");
        Assert.True(Retire < AppUnlock, "a second deploy of this app waits for the old colour to stop");
    }

    [Fact]
    public async Task Port_mode_waits_for_the_old_container_to_finish_before_the_new_one_takes_the_port()
    {
        var fs = new FakeFileSystem();
        var inner = new FakeProcessRunner { CaptureHandler = Captures() };
        var runner = new SlowStop(inner, "shop", reportsBeforeExit: 1, logs: DrainLine);
        var console = new StringConsole();
        var command = CreateOn(fs, inner, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = inner.Invocations.ToList();
        int Stop = all.FindIndex(i => i.Arguments.SequenceEqual(DockerCommands.BuildStopArguments("deploy@box", "shop", ShutdownBudget.DefaultStopTimeout)));
        int Start = all.FindIndex(i => i.Arguments.Contains("run") && i.Arguments.Contains("shop:current"));
        Assert.True(Stop >= 0 && Stop < Start);
        Assert.Contains("shop is finishing 1 job: AtPump (3m12s)", console.OutText, StringComparison.Ordinal);
    }

    private static DeployCommand CreateOn(FakeFileSystem fs, FakeProcessRunner inner, IProcessRunner runner, StringConsole console)
    {
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        FakeProxy.Wire(inner, fs);
        return new DeployCommand(console, fs, runner, WorkingDir)
        {
            ReadinessDelay = TimeSpan.Zero,
            ReadinessAttempts = 1,
            PreStopDrainDelay = TimeSpan.Zero,
            LockPollDelay = TimeSpan.Zero,
            StopReportDelay = TimeSpan.Zero,
        };
    }

    /// <summary>
    /// A box whose <c>docker stop</c> of <paramref name="container"/> takes a while: it returns only after the deploy
    /// has read the container's log <paramref name="reportsBeforeExit"/> times. Everything else is <paramref name="inner"/>.
    /// </summary>
    private sealed class SlowStop(FakeProcessRunner inner, string container, int reportsBeforeExit, string logs) : IProcessRunner
    {
        private readonly TaskCompletionSource<int> _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _reports;

        public async Task<int> RunAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
        {
            var exit = await inner.RunAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
            return arguments.Contains("stop") && arguments[^1] == container ? await _stopped.Task.ConfigureAwait(false) : exit;
        }

        public Task<int> RunTeeAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, Action<string> onLine, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null) =>
            inner.RunTeeAsync(fileName, arguments, workingDirectory, onLine, cancellationToken, environment);

        public async Task<ProcessResult> CaptureAsync(string fileName, IReadOnlyList<string> arguments, string? workingDirectory, CancellationToken cancellationToken, IReadOnlyDictionary<string, string>? environment = null)
        {
            var result = await inner.CaptureAsync(fileName, arguments, workingDirectory, cancellationToken, environment).ConfigureAwait(false);
            if (!arguments.Contains("logs") || arguments[^1] != container || _stopped.Task.IsCompleted)
            {
                return result;
            }

            if (Interlocked.Increment(ref _reports) >= reportsBeforeExit)
            {
                _stopped.TrySetResult(0);
            }

            return new ProcessResult(0, logs, string.Empty);
        }
    }
}
