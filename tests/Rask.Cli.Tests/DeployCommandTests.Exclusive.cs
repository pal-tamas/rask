using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// A new image whose pending migrations rewrite the rows — Rask's time-ordered Guid rewrite on SQL Server — is never
/// started beside the old version: the deploy asks it <c>rask db pending</c> first, and stops the old one before it.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string ExclusivePending = "20261127000000_AddDrivers\n20261127000001_OrderGuidsByTime exclusive\n";

    [Fact]
    public async Task An_exclusive_migration_stops_the_old_colour_before_the_new_one_starts_and_says_why()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Pending(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"), ExclusivePending) };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        var asked = all.FindIndex(static i => i.Arguments.Contains("--rm") && i.Arguments.Contains("pending"));
        var stopped = all.FindIndex(static i => i.Arguments.Contains("stop") && i.Arguments.Contains("demo-blue"));
        var started = all.FindIndex(static i => i.Arguments.Contains("-d") && i.Arguments.Contains("demo-green"));
        Assert.True(asked >= 0 && asked < stopped && stopped < started, "asked, then the old colour stopped, then the new one started");
        Assert.Contains("20261127000001_OrderGuidsByTime rewrites what the rows hold", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("stop-then-start", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_an_exclusive_migration_the_old_colour_keeps_serving_until_the_new_one_has_the_traffic()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Pending(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"), "20261127000000_AddDrivers\n") };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        var stopped = all.FindIndex(static i => i.Arguments.Contains("stop") && i.Arguments.Contains("demo-blue"));
        var started = all.FindIndex(static i => i.Arguments.Contains("-d") && i.Arguments.Contains("demo-green"));
        Assert.True(started >= 0 && started < stopped, "the new colour started while the old one still served");
        Assert.DoesNotContain("stop-then-start", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_first_deploy_has_no_old_colour_to_ask_about()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Pending(Captures(), ExclusivePending) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(runner.Invocations, static i => i.Arguments.Contains("--rm") && i.Arguments.Contains("pending"));
    }

    [Fact]
    public async Task A_failed_start_after_an_exclusive_migration_is_not_rolled_back_onto_the_old_code()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Pending(Captures(), ExclusivePending),
            RunHandler = static args => args.Contains("curlimages/curl:8.23.0") ? 1 : 0, // the probe fails
        };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "shop", "--port", "9000"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, static i => i.Arguments.Contains("-d") && i.Arguments.Contains("shop:previous"));
        Assert.Contains("nothing serves it now", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pending_answer_s_exclusive_lines_name_the_migrations()
    {
        var exclusive = DeployCommand.ExclusiveIn(ExclusivePending + "\n  \n");

        Assert.Equal(["20261127000001_OrderGuidsByTime"], exclusive);
        Assert.Empty(DeployCommand.ExclusiveIn(""));
    }

    [Fact]
    public void The_new_image_is_asked_on_the_app_s_network_and_volume_and_runs_nothing_else()
    {
        var args = DockerCommands.BuildPendingMigrationsArguments("deploy@box", "demo", "demo.example.com", ["Rask__ConnectionStrings__App=Server=db"]);

        Assert.Equal(["-H", "ssh://deploy@box", "run", "--rm", "--name", "demo-pending-migrations", "--network", DeployCommand.Network], args.Take(8));
        Assert.Equal(["demo:current", "rask", "db", "pending"], args.TakeLast(4));
        Assert.Contains("demo-data:/data", args);
        Assert.Contains("Rask__ConnectionStrings__App=Server=db", args);
        Assert.DoesNotContain("Rask__ConnectionStrings__App=Data Source=/data/app.db", args);
        Assert.DoesNotContain("-p", args);
        Assert.DoesNotContain("--restart", args);
    }

    // The deploy flow's captures, with the new image answering `rask db pending` with `pending`.
    private static Func<IReadOnlyList<string>, ProcessResult> Pending(Func<IReadOnlyList<string>, ProcessResult> captures, string pending) => args =>
        args.Count > 3 && args[^1] == "pending" ? new ProcessResult(0, pending, string.Empty) : captures(args);
}
