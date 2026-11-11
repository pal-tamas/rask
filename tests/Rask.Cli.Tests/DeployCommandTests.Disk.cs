using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// A deploy gives the box its disk back: once the new version serves, under the box's lock, it removes the images
/// earlier deploys left untagged and trims the build cache — never with <c>-a</c>, which would delete the
/// <c>:previous</c> images rollback restores — and before it builds, it refuses a box too full to build on.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const long Gigabyte = 1024L * 1024 * 1024;

    private static bool IsDiskRead(IReadOnlyList<string> args) =>
        args.Count > 0 && string.Equals(args[^1], HostDisk.Script, StringComparison.Ordinal);

    private static ProcessResult Disk(long freeBytes, long sizeBytes = 100 * Gigabyte) =>
        new(0, $"freekb={freeBytes / 1024}\nsizekb={sizeBytes / 1024}\nmount=/\n", string.Empty);

    private static Func<IReadOnlyList<string>, ProcessResult> OnDisk(ProcessResult disk, Func<IReadOnlyList<string>, ProcessResult> captures) => args =>
        IsDiskRead(args) ? disk : captures(args);

    private static int IndexOf(List<ProcessInvocation> all, IReadOnlyList<string> args) =>
        all.FindIndex(i => i.Arguments.SequenceEqual(args));

    [Fact]
    public async Task A_deploy_that_serves_prunes_dangling_images_and_the_build_cache_under_the_box_lock()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        int Check = all.FindIndex(i => i.Arguments.Contains("--resolve"));
        int Retire = IndexOf(all, DockerCommands.BuildStopArguments("deploy@box", "demo-blue", ShutdownBudget.DefaultStopTimeout));
        int Lock = all.FindLastIndex(i => i.Arguments.Contains(ProxyCommands.LockContainer) && i.Arguments.Contains("run"));
        int Images = IndexOf(all, PruneCommands.BuildImagePruneArguments("deploy@box"));
        int Cache = IndexOf(all, PruneCommands.BuildBuilderPruneArguments("deploy@box"));
        int Unlock = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", ProxyCommands.LockContainer]);
        int AppUnlock = all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", "rask-deploy-lock-demo"]);
        Assert.True(Check < Retire && Retire < Lock, "only once the route is checked and the old colour is gone");
        Assert.True(Lock < Images && Images < Cache && Cache < Unlock, "both prunes run under the box's lock");
        Assert.True(Unlock < AppUnlock, "and under the app's own");
    }

    [Fact]
    public void The_image_prune_never_takes_all_and_the_cache_prune_always_does()
    {
        var host = "deploy@box";

        var images = PruneCommands.BuildImagePruneArguments(host);
        var cache = PruneCommands.BuildBuilderPruneArguments(host);
        var legacy = PruneCommands.BuildBuilderPruneArguments(host, PruneCommands.LegacyBuildCacheFlag);

        // image prune -a would delete every :previous (the rollback image); dangling only.
        Assert.Equal(["-H", "ssh://deploy@box", "image", "prune", "-f"], images);
        Assert.DoesNotContain(images, arg => arg is "-a" or "-af" or "-fa" or "--all");
        // builder prune removes no image, and without -a it skips the shared records that are most of the cache.
        Assert.Equal(["-H", "ssh://deploy@box", "builder", "prune", "-af", "--reserved-space", "20GB"], cache);
        Assert.Equal(["-H", "ssh://deploy@box", "builder", "prune", "-af", "--keep-storage", "20GB"], legacy);
    }

    [Fact]
    public async Task A_port_deploy_that_serves_prunes_too()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(new FakeFileSystem(), runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        var all = runner.Invocations.ToList();
        int Lock = all.FindIndex(i => i.Arguments.Contains(ProxyCommands.LockContainer) && i.Arguments.Contains("run"));
        int Images = IndexOf(all, PruneCommands.BuildImagePruneArguments("deploy@box"));
        Assert.True(Lock >= 0 && Lock < Images, "the box's lock is taken even though a port deploy has no proxy to switch");
    }

    [Fact]
    public async Task A_deploy_whose_new_version_fails_its_health_check_prunes_nothing()
    {
        var runner = new FakeProcessRunner
        {
            CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")),
            RunHandler = args => args.Contains("container:demo-green") ? 1 : 0,
        };
        var command = Create(new FakeFileSystem(), runner, new StringConsole());

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune"));
    }

    [Fact]
    public async Task A_deploy_whose_build_fails_prunes_nothing()
    {
        var runner = new FakeProcessRunner
        {
            CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")),
            RunHandler = args => args.Contains("build") ? 1 : 0,
        };
        var command = Create(new FakeFileSystem(), runner, new StringConsole());

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune"));
    }

    [Fact]
    public async Task What_the_prunes_freed_is_said()
    {
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var pruned = false;
        ProcessResult PruneCache()
        {
            pruned = true;
            return new ProcessResult(0, "ID\tRECLAIMABLE\tSIZE\tLAST ACCESSED\nx1\ttrue\t1GB\t3 days ago\nTotal:\t12.3GB\n", string.Empty);
        }

        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
                IsDiskRead(args) ? Disk(pruned ? 60 * Gigabyte : 12 * Gigabyte, 368 * Gigabyte)
                : args.Contains("image") && args.Contains("prune") ? new ProcessResult(0, "Deleted Images:\ndeleted: sha256:abc\n\nTotal reclaimed space: 1.2GB\n", string.Empty)
                : args.Contains("builder") ? PruneCache()
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Reclaimed 1.2GB of old images and 12.3GB of build cache. Now 64.4 GB free of 395 GB on / (51.5 GB freed).", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_docker_client_without_the_new_cache_flag_is_asked_with_the_old_one()
    {
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains(PruneCommands.BuildCacheFlag)
                ? new ProcessResult(125, string.Empty, "unknown flag: --reserved-space\nSee 'docker builder prune --help'.\n")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.SequenceEqual(PruneCommands.BuildBuilderPruneArguments("deploy@box", "--keep-storage")));
        Assert.DoesNotContain("Cleaning up failed", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_prune_that_fails_is_a_warning_and_the_deploy_still_succeeds()
    {
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("builder")
                ? new ProcessResult(1, string.Empty, "Error response from daemon: a prune operation is already running\n")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Cleaning up failed (docker builder prune: Error response from daemon: a prune operation is already running)", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("The deploy itself succeeded", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box image prune -f", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments is [.., "rm", "-f", ProxyCommands.LockContainer]);
    }

    [Fact]
    public async Task A_prune_that_stalls_is_a_warning_and_the_box_lock_is_still_let_go()
    {
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args.Contains("image") && args.Contains("prune")
                ? throw new TimeoutException("`docker image` did not finish within 4m")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Cleaning up failed (`docker image` did not finish within 4m)", console.ErrorText, StringComparison.Ordinal);
        var all = runner.Invocations.ToList();
        Assert.True(IndexOf(all, PruneCommands.BuildImagePruneArguments("deploy@box")) < all.FindLastIndex(i => i.Arguments is [.., "rm", "-f", ProxyCommands.LockContainer]));
        Assert.DoesNotContain(all, i => i.Arguments.Contains("builder"));
    }

    [Fact]
    public async Task While_another_deploy_keeps_the_box_lock_the_prune_is_skipped_not_waited_out()
    {
        var takes = 0;
        var scripted = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"));
        var runner = new FakeProcessRunner
        {
            // The switch gets the lock; from then on another app's deploy holds it.
            CaptureHandler = args => args.Contains("sleep") && ++takes > 1
                ? new ProcessResult(125, string.Empty, "Conflict. The container name \"/rask-deploy-lock\" is already in use")
                : scripted(args),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(1 + DeployCommand.CleanUpLockAttempts, takes);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune"));
        Assert.Contains("Skipped the clean-up — the next deploy does it.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_box_too_full_to_build_on_fails_before_anything_moves_and_says_what_to_run()
    {
        var runner = new FakeProcessRunner { CaptureHandler = OnDisk(Disk(2 * Gigabyte, 368 * Gigabyte), OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"))) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("box is nearly out of disk: 2.1 GB free of 395 GB on /. Nothing was changed.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box image prune -f", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box builder prune -af --keep-storage 20GB", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("tag") || i.Arguments.Contains("build"));
    }

    [Fact]
    public async Task A_box_low_on_disk_is_warned_about_and_still_deployed_to()
    {
        var runner = new FakeProcessRunner { CaptureHandler = OnDisk(Disk(6 * Gigabyte, 368 * Gigabyte), OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"))) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("box is low on disk: 6.4 GB free of 395 GB on /.", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(runner.Invocations, i => i.Arguments.SequenceEqual(PruneCommands.BuildImagePruneArguments("deploy@box")));
    }

    [Fact]
    public async Task A_disk_that_cannot_be_read_does_not_stop_the_deploy()
    {
        var runner = new FakeProcessRunner { CaptureHandler = OnDisk(new ProcessResult(255, string.Empty, "df: not found"), OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"))) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("out of disk", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain("low on disk", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(2L, 368L, true, true)]     // under 3 GiB: refused
    [InlineData(5L, 368L, false, true)]    // under 10 GiB on a big disk: warned
    [InlineData(12L, 368L, false, false)]  // plenty
    [InlineData(5L, 20L, false, false)]    // a fifth of a small disk is enough not to nag
    [InlineData(3L, 20L, false, true)]     // but under a fifth of it is warned
    public void The_disk_guard_refuses_below_3_GiB_and_warns_below_10_GiB_or_a_fifth_of_a_small_disk(long freeGib, long sizeGib, bool tooLow, bool low)
    {
        var disk = new DiskSpace(freeGib * Gigabyte, sizeGib * Gigabyte, "/");

        var (refused, warned) = (disk.IsTooLow, disk.IsLow);

        Assert.Equal(tooLow, refused);
        Assert.Equal(low, warned);
    }

    [Fact]
    public void The_disk_read_takes_the_tighter_of_dockers_and_containerds_roots_from_df()
    {
        var output = "freekb=18874368\nsizekb=385875968\nmount=/\n";

        var disk = HostDisk.Parse(output);

        Assert.Equal(new DiskSpace(18874368L * 1024, 385875968L * 1024, "/"), disk);
        Assert.Contains("df -Pk ${d:+\"$d\"} /var/lib/containerd", HostDisk.Script, StringComparison.Ordinal);
        Assert.Null(HostDisk.Parse(string.Empty));
        Assert.Null(HostDisk.Parse("freekb=10\nsizekb=5\n"));
    }

    [Theory]
    [InlineData("Deleted Images:\ndeleted: sha256:1\n\nTotal reclaimed space: 98.2GB\n", "98.2GB")]
    [InlineData("ID\tRECLAIMABLE\tSIZE\tLAST ACCESSED\nTotal:\t280.6GB\n", "280.6GB")]
    [InlineData("Total reclaimed space: 0B\n", "0B")]
    [InlineData("", null)]
    public void What_a_prune_freed_is_read_from_its_total_line(string output, string? reclaimed)
    {
        var text = output;

        var read = PruneCommands.Reclaimed(text);

        Assert.Equal(reclaimed, read);
    }

    [Fact]
    public void Both_prunes_end_within_the_box_locks_lease()
    {
        var deadlines = DeployDeadlines.Default;

        var images = deadlines.For(PruneCommands.BuildImagePruneArguments("deploy@box"));
        var cache = deadlines.For(PruneCommands.BuildBuilderPruneArguments("deploy@box"));

        Assert.Equal(DeployDeadlines.Prune, images);
        Assert.Equal(DeployDeadlines.Prune, cache);
        Assert.True(images + cache < TimeSpan.FromSeconds(ProxyCommands.LockLeaseSeconds));
    }

    [Fact]
    public async Task Status_says_how_much_disk_is_free_and_what_docker_could_reclaim()
    {
        var listing = Captures("demo-blue\tdemo\tdemo.example.com\tblue\tUp 2 hours\t\n");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = OnDisk(Disk(18 * Gigabyte, 368 * Gigabyte), args => args.Contains("df")
                ? new ProcessResult(0, "Images\t98.2GB (81%)\nContainers\t0B (0%)\nLocal Volumes\t0B (0%)\nBuild Cache\t8.1GB\n", string.Empty)
                : listing(args)),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Disk: 19.3 GB free of 395 GB on /. Reclaimable: 98.2GB (81%) of images, 8.1GB of build cache", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune")); // status changes nothing
    }

    [Fact]
    public async Task The_dry_run_shows_the_prunes_and_never_an_image_prune_of_all()
    {
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), new FakeProcessRunner(), console);

        var exit = await command.ExecuteAsync([.. DomainDeploy, "--dry-run"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("docker -H ssh://deploy@box image prune -f\n", console.OutText.ReplaceLineEndings("\n"), StringComparison.Ordinal);
        Assert.Contains("docker -H ssh://deploy@box builder prune -af --reserved-space 20GB", console.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("image prune -a", console.OutText, StringComparison.Ordinal);
    }
}
