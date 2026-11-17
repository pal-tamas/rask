using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// <c>rask host clean</c> runs the two prunes a deploy runs once it serves, on demand, under the same box lock —
/// waiting while a deploy holds it, as a deploy would, and saying what was freed.
/// </summary>
public sealed class HostCommandTests
{
    private const string WorkingDir = "/proj";
    private const long Gigabyte = 1024L * 1024 * 1024;
    private const string LockConflict = "Conflict. The container name \"/rask-deploy-lock\" is already in use by container abc";

    private static bool IsDiskRead(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[^1], HostDisk.Script, StringComparison.Ordinal);

    private static bool IsLockTake(IReadOnlyList<string> args) =>
        args.Contains(ProxyCommands.LockContainer) && args.Contains("sleep");

    private static HostCommand Create(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console, int lockAttempts = 5) =>
        new(console, fs, runner, WorkingDir) { LockPollDelay = TimeSpan.Zero, LockAttempts = lockAttempts };

    private static Func<IReadOnlyList<string>, ProcessResult> Box(Func<int, ProcessResult?>? lockTake = null)
    {
        var takes = 0;
        var pruned = false;
        return args =>
            IsDiskRead(args) ? new ProcessResult(0, $"freekb={(pruned ? 60 : 12) * Gigabyte / 1024}\nsizekb={368 * Gigabyte / 1024}\nmount=/\n", string.Empty)
            : IsLockTake(args) ? lockTake?.Invoke(++takes) ?? new ProcessResult(0, "lockid\n", string.Empty)
            : args.Contains("image") && args.Contains("prune") ? new ProcessResult(0, "Total reclaimed space: 1.2GB\n", string.Empty)
            : args.Contains("builder") ? PruneCache()
            : new ProcessResult(0, string.Empty, string.Empty);

        ProcessResult PruneCache()
        {
            pruned = true;
            return new ProcessResult(0, "Total:\t12.3GB\n", string.Empty);
        }
    }

    private static int IndexOf(List<ProcessInvocation> all, Func<IReadOnlyList<string>, bool> match) =>
        all.FindIndex(i => match(i.Arguments));

    [Fact]
    public async Task Clean_prunes_both_under_the_box_lock_and_says_what_was_freed()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Box() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["clean", "--host", "deploy@box"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        var taken = IndexOf(all, IsLockTake);
        var images = IndexOf(all, args => args.SequenceEqual(PruneCommands.BuildImagePruneArguments("deploy@box")));
        var cache = IndexOf(all, args => args.SequenceEqual(PruneCommands.BuildBuilderPruneArguments("deploy@box", "20GB")));
        var released = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", "lockid"]);
        Assert.True(taken >= 0 && taken < images && images < cache && cache < released, "both prunes run inside the box's lock");
        Assert.Contains("Reclaimed 1.2GB of old images and 12.3GB of build cache. Now 64.4 GB free of 395 GB on / (51.5 GB freed).", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Clean_takes_the_host_and_the_kept_build_cache_from_deploy_json()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","buildCacheKeep":"40GB"}""");
        var runner = new FakeProcessRunner { CaptureHandler = Box() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["clean"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.SequenceEqual(PruneCommands.BuildBuilderPruneArguments("deploy@box", "40GB")));
    }

    [Fact]
    public async Task Clean_waits_while_a_deploy_holds_the_box_lock_then_prunes()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Box(take => take <= 2 ? new ProcessResult(125, string.Empty, LockConflict) : null) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["clean", "--host", "deploy@box"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Another deploy is using this box's shared proxy or disk", console.OutText, StringComparison.Ordinal);
        Assert.Equal(3, runner.Invocations.Count(i => IsLockTake(i.Arguments)));
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("prune"));
    }

    [Fact]
    public async Task Clean_gives_up_without_pruning_when_the_box_lock_stays_held()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Box(_ => new ProcessResult(125, string.Empty, LockConflict)) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console, lockAttempts: 3);

        var exit = await command.ExecuteAsync(["clean", "--host", "deploy@box"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(3, runner.Invocations.Count(i => IsLockTake(i.Arguments)));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune"));
        Assert.Contains("Gave up waiting for the deploy lock rask-deploy-lock on deploy@box", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prune_that_fails_fails_the_clean_and_says_what_to_run_by_hand()
    {
        var box = Box();
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("builder")
                ? new ProcessResult(1, string.Empty, "Error response from daemon: a prune operation is already running\n")
                : box(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["clean", "--host", "deploy@box"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Cleaning up failed (docker builder prune: Error response from daemon: a prune operation is already running)", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box builder prune -af --keep-storage 20GB", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments is [.., "rm", "-f", "lockid"]);
    }

    [Fact]
    public async Task The_dry_run_prints_the_commands_and_runs_none()
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["clean", "--host", "deploy@box", "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Empty(runner.Invocations);
        Assert.Contains("docker -H ssh://deploy@box image prune -f", console.OutText, StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box builder prune -af --reserved-space 20GB", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("image prune -a", console.OutText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(new string[0], "Specify a 'rask host' action: clean.")]
    [InlineData(new[] { "clen" }, "Unknown 'rask host' action 'clen'. Did you mean 'clean'?")]
    [InlineData(new[] { "clean" }, "No host to clean. Pass --host user@host")]
    [InlineData(new[] { "clean", "--host", "-oProxyCommand=sh" }, "-oProxyCommand=sh")]
    [InlineData(new[] { "clean", "--host", "deploy@box", "extra" }, "Unexpected argument 'extra'.")]
    public async Task A_clean_that_cannot_start_is_a_usage_error_that_says_why(string[] args, string said)
    {
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(args, CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains(said, console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task A_disk_setting_that_cannot_be_used_stops_the_clean_before_it_connects()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","buildCacheKeep":"plenty"}""");
        var runner = new FakeProcessRunner();
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync(["clean"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("\"buildCacheKeep\": \"plenty\" isn't a size", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }
}
