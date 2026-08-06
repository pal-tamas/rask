using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// The day-two verbs: <c>rask deploy status | logs | rollback</c>. The builders and parsers are asserted
/// directly; the behaviour that only a real host can prove (that an image tag survives a build which
/// reuses it, so a rollback has something to restore) lives in <see cref="DeployHostE2ETests"/>.
/// </summary>
public sealed class DeployOpsTests
{
    private const string WorkingDir = "/proj";

    private static DeployCommand Create(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console)
    {
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        return new DeployCommand(console, fs, runner, WorkingDir) { ReadinessDelay = TimeSpan.Zero, ReadinessAttempts = 1 };
    }

    /// <summary>A ready host, a scripted `docker ps` listing, containers up, and an image for a tag query.</summary>
    private static Func<IReadOnlyList<string>, ProcessResult> Captures(string psListing = "", bool hasPrevious = true) => args =>
        DeployCommandTests.IsHostProbe(args) ? new ProcessResult(0, DeployCommandTests.ReadyHostProbe, string.Empty)
        : args.Contains("inspect") && args.Contains("image") ? new ProcessResult(hasPrevious ? 0 : 1, hasPrevious ? "sha256:0123456789abcdef\n" : string.Empty, string.Empty)
        : args.Contains("ps") ? new ProcessResult(0, psListing, string.Empty)
        : args.Contains("inspect") ? new ProcessResult(0, "true\n", string.Empty)
        : new ProcessResult(0, string.Empty, string.Empty);

    // ── Builders / parsers ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Build_tags_the_image_current_and_latest()
    {
        // :current is what containers run and what the next deploy moves aside; :latest is kept so the
        // box still reads the way a person expects from `docker images`.
        var args = DeployCommand.BuildBuildArguments("deploy@box", "shop", "/proj/Dockerfile", "/proj");

        Assert.Equal(
            ["-H", "ssh://deploy@box", "build", "-t", "shop:current", "-t", "shop:latest", "-f", "/proj/Dockerfile", "/proj"],
            args);
    }

    [Fact]
    public void Retag_moves_a_tag_without_rebuilding() =>
        Assert.Equal(
            ["-H", "ssh://deploy@box", "tag", "shop:current", "shop:previous"],
            DeployCommand.BuildRetagArguments("deploy@box", "shop", "current", "previous"));

    [Fact]
    public void Run_arguments_can_start_a_chosen_tag() =>
        Assert.Contains(
            "shop:previous",
            DeployCommand.BuildRunArguments("deploy@box", "shop", domain: null, color: null, 9000, [], 8080, "previous"));

    [Theory]
    [InlineData("50", false, new[] { "logs", "--tail", "50" })]
    [InlineData("all", false, new[] { "logs", "--tail", "all" })]
    [InlineData("10", true, new[] { "logs", "--tail", "10", "--follow" })]
    public void Logs_arguments_carry_tail_and_follow(string tail, bool follow, string[] expected)
    {
        var args = DeployCommand.BuildLogsArguments("deploy@box", "shop-blue", tail, follow);

        Assert.Equal([.. new[] { "-H", "ssh://deploy@box" }.Concat(expected).Append("shop-blue")], args);
    }

    [Fact]
    public void ParseStatusRows_reads_the_listing_and_skips_malformed_lines()
    {
        var rows = DeployCommand.ParseStatusRows(
            "shop-blue\tshop\tshop.example.com\tblue\tUp 2 hours\t\n"
            + "api\tapi\t<no value>\t<no value>\tUp 5 minutes\t0.0.0.0:9000->8080/tcp\n"
            + "garbage-line\n");

        Assert.Equal(2, rows.Count);
        Assert.Equal(new StatusRow("shop-blue", "shop", "shop.example.com", "blue", "Up 2 hours", string.Empty), rows[0]);
        Assert.Equal("0.0.0.0:9000->8080/tcp", rows[1].Ports);
    }

    // ── status ──────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Status_lists_every_app_on_the_box()
    {
        // Apps share a host and a proxy, so "what else is here" is part of the answer — it's how you
        // notice the second app you'd forgotten was holding a domain.
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\tUp 2 hours\t\napi\tapi\t<no value>\t<no value>\tUp 5 minutes\t0.0.0.0:9000->8080/tcp\n"),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("https://shop.example.com", console.OutText, StringComparison.Ordinal);
        Assert.Contains("0.0.0.0:9000->8080/tcp", console.OutText, StringComparison.Ordinal);
        Assert.Contains("Up 2 hours", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("build")); // status changes nothing
    }

    [Fact]
    public async Task Status_on_an_empty_host_says_so_rather_than_printing_an_empty_table()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Nothing deployed", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_says_whether_a_rollback_is_possible()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\tUp 2 hours\t\n", hasPrevious: false) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        await command.ExecuteAsync(["status", "--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Contains("nothing to go back to", console.OutText, StringComparison.Ordinal);
    }

    // ── logs ────────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Logs_target_the_live_container_for_this_app()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-green\tshop\tshop.example.com\tgreen\t8080\n") };
        var command = Create(new FakeFileSystem(), runner, new StringConsole());

        var exit = await command.ExecuteAsync(["logs", "--host", "deploy@box", "--name", "shop", "--tail", "25"], CancellationToken.None);

        Assert.Equal(0, exit);
        var logs = runner.Invocations.Single(i => i.Arguments.Contains("logs"));
        Assert.Equal(["-H", "ssh://deploy@box", "logs", "--tail", "25", "shop-green"], logs.Arguments);
    }

    [Fact]
    public async Task Logs_for_an_app_that_is_not_running_explain_rather_than_show_nothing()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["logs", "--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("isn't running", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-5")]
    [InlineData("lots")]
    public async Task An_invalid_tail_is_rejected(string value)
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\t8080\n") };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["logs", "--host", "deploy@box", "--name", "shop", "--tail", value], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("--tail must be", console.ErrorText, StringComparison.Ordinal);
    }

    // ── rollback ────────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Rollback_starts_the_previous_image_and_swaps_the_tags()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\t8080\n") };
        var console = new StringConsole();
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","name":"shop","domain":"shop.example.com"}""");
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["rollback"], CancellationToken.None);

        Assert.Equal(0, exit);

        // The new colour runs the PREVIOUS image, and nothing was rebuilt.
        var run = runner.Invocations.Single(i => i.Arguments.Contains("rask.app=shop"));
        Assert.Contains("shop:previous", run.Arguments);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("build"));

        // ...and the tags were exchanged, so :current still names what is serving and rolling back again
        // returns to the version we just left rather than repeating this one.
        var tags = runner.Invocations.Where(i => i.Arguments.Contains("tag")).Select(i => string.Join(' ', i.Arguments)).ToArray();
        Assert.Contains(tags, t => t.EndsWith("tag shop:current shop:rollback-swap", StringComparison.Ordinal));
        Assert.Contains(tags, t => t.EndsWith("tag shop:previous shop:current", StringComparison.Ordinal));
        Assert.Contains(tags, t => t.EndsWith("tag shop:rollback-swap shop:previous", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Rollback_without_a_previous_image_refuses_and_changes_nothing()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures("shop-blue\tshop\tshop.example.com\tblue\t8080\n", hasPrevious: false) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["rollback", "--host", "deploy@box", "--name", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("nothing to roll back to", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("run"));
    }

    // ── argument discipline ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_unknown_action_lists_the_real_ones()
    {
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["restart"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("Unknown 'rask deploy' action 'restart'.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("status, logs, rollback", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new[] { "status", "--domain", "x.example.com" }, "--domain")]
    [InlineData(new[] { "rollback", "--dockerfile", "/tmp/Dockerfile" }, "--dockerfile")]
    [InlineData(new[] { "logs", "--github-actions" }, "--github-actions")]
    [InlineData(new[] { "status", "--dry-run" }, "--dry-run")]
    public async Task Options_that_belong_to_a_deploy_are_refused_not_ignored(string[] args, string option)
    {
        // Accepting and silently ignoring them would leave the user believing something happened.
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync([.. args, "--host", "deploy@box"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains(option, console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Logs_only_options_are_refused_on_a_plain_deploy()
    {
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--follow"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("only apply to `rask deploy logs`", console.ErrorText, StringComparison.Ordinal);
    }
}
