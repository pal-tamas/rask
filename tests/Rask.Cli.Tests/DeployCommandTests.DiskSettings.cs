using System.Text.Json;
using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The box's disk is the app's to tune: <c>"buildCacheKeep"</c>, <c>"minFreeDisk"</c> and <c>"warnFreeDisk"</c> in
/// <c>.rask/deploy.json</c>, <c>--no-prune</c> for one deploy, and the <c>disk</c> of <c>rask deploy status --json</c>.
/// </summary>
public sealed partial class DeployCommandTests
{
    private const string RememberedDomainDeploy = """{"host":"deploy@box","name":"demo","domain":"demo.example.com",""";

    [Fact]
    public async Task The_build_cache_keep_in_deploy_json_is_what_the_prune_keeps()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + "\"buildCacheKeep\":\"40GB\"}");
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.SequenceEqual(PruneCommands.BuildBuilderPruneArguments("deploy@box", "40GB")));
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("20GB"));
    }

    [Fact]
    public async Task A_raised_min_free_disk_refuses_a_box_the_default_would_build_on()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + "\"minFreeDisk\":\"50GB\",\"warnFreeDisk\":\"60GB\"}");
        var runner = new FakeProcessRunner { CaptureHandler = OnDisk(Disk(20 * Gigabyte, 368 * Gigabyte), OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"))) };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("box is nearly out of disk: 21.5 GB free of 395 GB on /. Nothing was changed.", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("build"));
    }

    [Fact]
    public async Task A_warn_free_disk_replaces_the_fifth_of_the_disk_rule()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + "\"warnFreeDisk\":\"100GB\"}");
        var runner = new FakeProcessRunner { CaptureHandler = OnDisk(Disk(50 * Gigabyte, 368 * Gigabyte), OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"))) };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("box is low on disk: 53.7 GB free of 395 GB on /.", console.ErrorText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"minFreeDisk\":\"lots\"", "\"minFreeDisk\": \"lots\" isn't a size")]
    [InlineData("\"buildCacheKeep\":\"-20GB\"", "\"buildCacheKeep\": \"-20GB\" isn't a size")]
    [InlineData("\"minFreeDisk\":\"8GB\",\"warnFreeDisk\":\"4GB\"", "\"warnFreeDisk\": \"4GB\" is below \"minFreeDisk\" (8GB)")]
    public async Task A_disk_setting_that_cannot_be_used_is_a_usage_error_before_anything_runs(string setting, string said)
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + setting + "}");
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains($".rask{Path.DirectorySeparatorChar}deploy.json: {said}", console.ErrorText, StringComparison.Ordinal);
        Assert.Empty(runner.Invocations);
    }

    [Fact]
    public async Task The_disk_settings_survive_the_deploy_that_saves_deploy_json_as_they_were_written()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + "\"buildCacheKeep\":\"40 GiB\",\"minFreeDisk\":\"5GB\",\"warnFreeDisk\":\"15GB\"}");
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync([], CancellationToken.None);

        Assert.Equal(0, exit);
        var saved = JsonSerializer.Deserialize(fs.Files[Path.GetFullPath("/proj/.rask/deploy.json")], DeployConfigJsonContext.Default.DeployConfig)!;
        Assert.Equal(("40 GiB", "5GB", "15GB"), (saved.BuildCacheKeep, saved.MinFreeDisk, saved.WarnFreeDisk));
    }

    [Fact]
    public async Task A_first_deploy_writes_no_disk_settings()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        var written = fs.Files[Path.GetFullPath("/proj/.rask/deploy.json")];
        Assert.DoesNotContain("buildCacheKeep", written, StringComparison.Ordinal);
        Assert.DoesNotContain("FreeDisk", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_deploy_with_no_prune_serves_and_leaves_the_box_as_it_was()
    {
        var runner = new FakeProcessRunner { CaptureHandler = OnFragmentLayout(Captures("demo-blue\tdemo\tdemo.example.com\tblue\n")) };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync([.. DomainDeploy, "--no-prune"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("prune"));
        Assert.Contains("Skipped the clean-up (--no-prune). `rask host clean --host deploy@box` runs it when you choose.", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_prune_on_an_action_is_a_usage_error()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--no-prune"], CancellationToken.None);

        Assert.Equal(CliCommand.UsageExitCode, exit);
        Assert.Contains("--no-prune doesn't apply to `rask deploy status`.", console.ErrorText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_dry_run_shows_the_kept_build_cache_and_drops_the_prunes_under_no_prune()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", RememberedDomainDeploy + "\"buildCacheKeep\":\"40GB\",\"minFreeDisk\":\"5GB\"}");
        var (kept, skipped) = (new StringConsole(), new StringConsole());

        var keptExit = await Create(fs, new FakeProcessRunner(), kept).ExecuteAsync(["--dry-run"], CancellationToken.None);
        var skippedExit = await Create(fs, new FakeProcessRunner(), skipped).ExecuteAsync(["--dry-run", "--no-prune"], CancellationToken.None);

        Assert.Equal((0, 0), (keptExit, skippedExit));
        Assert.Contains("docker -H ssh://deploy@box builder prune -af --reserved-space 40GB", kept.OutText, StringComparison.Ordinal);
        Assert.Contains("stops below 5.4 GB free, warns below 10.7 GB or a fifth of the disk", kept.OutText, StringComparison.Ordinal);
        Assert.DoesNotContain("prune", skipped.OutText.Replace("--no-prune", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_json_carries_the_disk_as_the_text_line_does()
    {
        var listing = Captures("demo-blue\tdemo\tdemo.example.com\tblue\tUp 2 hours\t\n");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = OnDisk(Disk(6 * Gigabyte, 368 * Gigabyte), args => args.Contains("df")
                ? new ProcessResult(0, "Images\t98.2GB (81%)\nContainers\t0B (0%)\nBuild Cache\t8.1GB\n", string.Empty)
                : listing(args)),
        };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--app", "demo", "--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(console.OutText);
        var disk = json.RootElement.GetProperty("disk");
        Assert.Equal("/", disk.GetProperty("mount").GetString());
        Assert.Equal(6 * Gigabyte, disk.GetProperty("freeBytes").GetInt64());
        Assert.Equal(368 * Gigabyte, disk.GetProperty("sizeBytes").GetInt64());
        Assert.True(disk.GetProperty("isLow").GetBoolean());
        Assert.False(disk.GetProperty("isTooLow").GetBoolean());
        Assert.Equal("98.2GB (81%)", disk.GetProperty("reclaimableImages").GetString());
        Assert.Equal("8.1GB", disk.GetProperty("reclaimableBuildCache").GetString());
    }

    [Fact]
    public async Task Status_json_has_no_disk_when_the_box_would_not_say()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--app", "demo", "--json"], CancellationToken.None);

        Assert.Equal(0, exit);
        using var json = JsonDocument.Parse(console.OutText);
        Assert.False(json.RootElement.TryGetProperty("disk", out _));
        Assert.Equal(0, json.RootElement.GetProperty("apps").GetArrayLength());
    }

    [Fact]
    public void An_explicit_warn_free_disk_replaces_the_fifth_of_the_disk_rule()
    {
        var policy = new DiskPolicy(20 * Gigabyte, 3 * Gigabyte, 8 * Gigabyte);
        var small = new DiskSpace(5 * Gigabyte, 20 * Gigabyte, "/");

        var (byDefault, bySetting) = (DiskPolicy.Default.IsLow(small), policy.IsLow(small));

        Assert.False(byDefault, "5 GiB is more than a fifth of a 20 GiB disk");
        Assert.True(bySetting, "but under the 8 GiB the app asked to be warned at");
        Assert.False(policy.IsTooLow(small));
    }
}
