using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// <c>rask db restore --remote</c> stops the app before it replaces the database. It stops it the way a deploy retires
/// a colour: within the app's <c>"stopTimeout"</c>, saying what it waits for, and killing it loudly only at the end.
/// </summary>
public sealed class DbRestoreDrainTests
{
    private const string Backup = "/proj/shop.db";

    private static (DbCommand Command, FakeFileSystem Fs) Create(IProcessRunner runner, StringConsole console, string config)
    {
        var fs = new FakeFileSystem();
        fs.Seed(Backup, "SQLite format 3");
        fs.Seed("/proj/.rask/deploy.json", config);
        return (new DbCommand(console, fs, runner, "/proj") { StopReportDelay = TimeSpan.Zero }, fs);
    }

    [Fact]
    public async Task A_restore_stops_the_app_within_its_stop_budget_and_says_what_it_waits_for()
    {
        var inner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "true\n", string.Empty) };
        var runner = new SlowStopRunner(inner, "shop", reportsBeforeExit: 2, logs: SlowStopRunner.DrainLine);
        var console = new StringConsole();
        var (command, _) = Create(runner, console, """{"host":"deploy@box","name":"shop","stopTimeout":"45m"}""");

        var exit = await command.ExecuteAsync(["restore", Backup, "--remote", "--yes"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = inner.Invocations.ToList();
        int Stop = all.FindIndex(i => i.Arguments.SequenceEqual(DockerCommands.BuildStopArguments("deploy@box", "shop", TimeSpan.FromMinutes(45))));
        int Start = all.FindIndex(i => i.Arguments.SequenceEqual(DbCommand.BuildStartArguments("deploy@box", "shop")));
        Assert.True(Stop >= 0 && Stop < Start, "the app is stopped within its budget before the restore, and started after");
        Assert.Contains("shop is finishing 1 job: AtPump (3m12s)", console.OutText, StringComparison.Ordinal);
        Assert.Contains(" of 45m", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_that_had_to_kill_the_app_says_so_loudly()
    {
        var inner = new FakeProcessRunner
        {
            CaptureHandler = args => new ProcessResult(0, args.Contains("{{.State.ExitCode}}") ? "137\n" : "true\n", string.Empty),
        };
        var runner = new SlowStopRunner(inner, "shop", reportsBeforeExit: 1, logs: SlowStopRunner.DrainLine);
        var console = new StringConsole();
        var (command, _) = Create(runner, console, """{"host":"deploy@box","name":"shop"}""");

        await command.ExecuteAsync(["restore", Backup, "--remote", "--yes"], CancellationToken.None);

        Assert.Contains("shop was still running when its 30m stop budget ran out and was killed", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("\"stopTimeout\"", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_restore_whose_app_is_not_there_refuses_and_changes_nothing()
    {
        var inner = new FakeProcessRunner
        {
            RunHandler = args => args.Contains("stop") ? 1 : 0,
            CaptureHandler = args => args.Contains("inspect") ? new ProcessResult(1, string.Empty, "Error: No such object: shop") : new ProcessResult(0, string.Empty, string.Empty),
        };
        var console = new StringConsole();
        var (command, _) = Create(inner, console, """{"host":"deploy@box","name":"shop"}""");

        var exit = await command.ExecuteAsync(["restore", Backup, "--remote", "--yes"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Refusing to restore", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(inner.Invocations, i => i.Arguments.Contains("cp"));
    }

    [Fact]
    public async Task A_restore_with_a_stop_budget_that_is_not_a_duration_refuses_before_stopping_anything()
    {
        var inner = new FakeProcessRunner();
        var console = new StringConsole();
        var (command, _) = Create(inner, console, """{"host":"deploy@box","name":"shop","stopTimeout":"soon"}""");

        var exit = await command.ExecuteAsync(["restore", Backup, "--remote", "--yes"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("stopTimeout", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(inner.Invocations, i => i.Arguments.Contains("stop"));
    }
}
