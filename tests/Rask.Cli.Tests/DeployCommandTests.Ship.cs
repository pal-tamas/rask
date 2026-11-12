using Rask.Cli.Commands;
using Rask.Cli.Scaffolding;

namespace Rask.Cli.Tests;

/// <summary>
/// The image is built here, for the box's platform, and streamed to the box (<c>docker save | gzip | docker load</c>), so
/// the box never builds; a machine with no Linux engine builds on the box as before. Every deploy names its release.
/// </summary>
public sealed partial class DeployCommandTests
{
    private static readonly DateTimeOffset DeployMoment = new(2026, 11, 11, 13, 12, 2, TimeSpan.Zero);

    // This machine answers `docker version` with `local`, the box with `box`; null for an engine that doesn't answer.
    private static Func<IReadOnlyList<string>, ProcessResult> WithEngines(string? local, string? box, Func<IReadOnlyList<string>, ProcessResult>? rest = null) => args =>
        args is ["version", "--format", _] ? Engine(local)
        : args is ["-H", _, "version", "--format", _] ? Engine(box)
        : (rest ?? Captures())(args);

    private static ProcessResult Engine(string? platform) =>
        platform is null ? new ProcessResult(1, string.Empty, "Cannot connect to the Docker daemon") : new ProcessResult(0, platform + "\n", string.Empty);

    private DeployCommand CreateAt(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console)
    {
        var command = Create(fs, runner, console);
        command.Clock = new FixedClock(DeployMoment);
        return command;
    }

    [Fact]
    public async Task A_deploy_builds_here_for_the_box_platform_and_streams_the_image_to_the_box()
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/arm64", "linux/amd64") };
        var console = new StringConsole();
        var command = CreateAt(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var runs = runner.Invocations.Where(i => !i.Captured).Select(i => i.Arguments).ToList();
        var build = runs.FindIndex(a => a.Contains("build"));
        var aside = runs.FindIndex(a => a is ["-H", "ssh://deploy@box", "tag", "shop:current", "shop:previous"]);
        var save = runs.FindIndex(a => a is ["save", "shop:current"]);
        var load = runs.FindIndex(a => a is ["-H", "ssh://deploy@box", "load"]);
        var latest = runs.FindIndex(a => a is ["-H", "ssh://deploy@box", "tag", "shop:current", "shop:latest"]);
        Assert.Equal(
            ["build", "--platform", "linux/amd64", "-t", "shop:current", "--label", "rask.release=v26.11.11.13.12.02", "-f", "/proj/Dockerfile", "/proj"],
            runs[build]);
        Assert.True(build < aside && aside < save && save + 1 == load && load < latest, string.Join("\n", runs.Select(a => string.Join(' ', a))));
        Assert.Contains(runner.Invocations, i => i.Captured && i.Arguments is ["image", "rm", "shop:current"]);
        Assert.DoesNotContain(runs, a => a.Contains("ssh://deploy@box") && a.Contains("build"));
        Assert.Contains("here for linux/amd64", console.OutText, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, "linux/amd64", "No Linux Docker engine answers on this machine")]
    [InlineData("windows/amd64", "linux/amd64", "No Linux Docker engine answers on this machine")]
    [InlineData("linux/amd64", null, "Could not read box's platform")]
    public async Task Without_both_engines_the_box_builds_as_before(string? local, string? box, string said)
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines(local, box) };
        var console = new StringConsole();
        var command = CreateAt(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments is ["-H", "ssh://deploy@box", "build", "-t", "shop:current", "-t", "shop:latest", "--label", "rask.release=v26.11.11.13.12.02", ..]);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("save") || i.Arguments.Contains("load"));
        Assert.Contains(said, console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_image_that_does_not_reach_the_box_fails_the_deploy_before_anything_starts()
    {
        var runner = new FakeProcessRunner
        {
            CaptureHandler = WithEngines("linux/amd64", "linux/amd64"),
            RunHandler = args => args.Contains("load") ? 1 : 0,
        };
        var console = new StringConsole();
        var command = CreateAt(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("Sending the image to box failed", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments.Contains("run") && i.Arguments.Contains("shop:current"));
        Assert.Contains(runner.Invocations, i => i.Arguments is ["image", "rm", "shop:current"]); // the copy here still goes
    }

    [Fact]
    public async Task The_container_is_told_the_release_the_deploy_named()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var command = CreateAt(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        var run = runner.Invocations.Single(i => !i.Captured && i.Arguments.Contains("run") && i.Arguments.Contains("shop:current")).Arguments;
        var at = run.ToList().IndexOf("Rask__Version=v26.11.11.13.12.02");
        Assert.True(at > 0 && run[at - 1] == "-e", string.Join(' ', run));
        Assert.DoesNotContain("Rask__Version", fs.ReadAllText("/proj/.rask/deploy.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_release_the_user_names_is_theirs_and_never_a_required_variable()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = WithEngines("linux/amd64", "linux/amd64") };
        var command = CreateAt(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--port", "9000", "--app", "shop", "--env", "Rask:Version=3f2a9c1"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i.Arguments.Contains("build") && i.Arguments.Contains("rask.release=3f2a9c1"));
        var run = runner.Invocations.Single(i => !i.Captured && i.Arguments.Contains("run") && i.Arguments.Contains("shop:current")).Arguments;
        Assert.DoesNotContain(run, a => a.StartsWith("Rask__Version=v", StringComparison.Ordinal));
        Assert.DoesNotContain("Rask__Version", fs.ReadAllText("/proj/.rask/deploy.json"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rollback_tells_the_container_the_release_its_image_was_built_as()
    {
        var fs = new FakeFileSystem();
        new DeployConfig { Host = "deploy@box", Name = "shop", Port = 9000 }.Save(fs, WorkingDir);
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
                args.Contains("image") && args.Contains("inspect") && args.Any(a => a.Contains("rask.release", StringComparison.Ordinal))
                    ? new ProcessResult(0, "v26.11.10.09.00.00\n", string.Empty)
                    : args.Contains("image") && args.Contains("inspect") ? new ProcessResult(0, "sha256:0123456789abcdef\n", string.Empty)
                    : Captures()(args),
        };
        var command = CreateAt(fs, runner, new StringConsole());

        var exit = await command.ExecuteAsync(["rollback"], CancellationToken.None);

        Assert.Equal(0, exit);
        var run = runner.Invocations.Single(i => !i.Captured && i.Arguments.Contains("run") && i.Arguments.Contains("shop:previous")).Arguments;
        Assert.Contains("Rask__Version=v26.11.10.09.00.00", run);
    }

    [Fact]
    public void A_release_is_named_by_the_utc_moment_of_the_deploy()
    {
        var moment = new DateTimeOffset(2026, 11, 11, 14, 12, 2, TimeSpan.FromHours(1));

        var name = ReleaseName.At(moment);

        Assert.Equal("v26.11.11.13.12.02", name);
    }

    [Theory]
    [InlineData(0, "linux/amd64\n", "linux/amd64")]
    [InlineData(0, "linux/arm64", "linux/arm64")]
    [InlineData(0, "windows/amd64", null)]
    [InlineData(0, "", null)]
    [InlineData(0, "linux/", null)]
    [InlineData(1, "linux/amd64", null)]
    public void Only_a_linux_engine_that_answered_names_a_platform(int exit, string output, string? platform)
    {
        var version = new ProcessResult(exit, output, string.Empty);

        var named = DeployCommand.LinuxPlatform(version);

        Assert.Equal(platform, named);
    }

    [Fact]
    public void Loading_an_image_has_as_long_as_a_build()
    {
        var deadlines = DeployDeadlines.Default;

        var load = deadlines.For(DockerCommands.BuildLoadArguments("deploy@box"));

        Assert.Equal(deadlines.Build, load);
    }

    [Fact]
    public async Task The_shipment_goes_through_the_open_tunnel_but_the_save_stays_here()
    {
        var runner = new FakeProcessRunner();
        var held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var routed = new TunnelRunner(held);
        var (tunnel, _) = await DockerTunnel.OpenAsync(held, "deploy@box", new ReadinessPoll { Interval = TimeSpan.Zero, MaxAttempts = 1 }, () => directory, CancellationToken.None);
        await using var open = tunnel!;
        routed.Tunnel = tunnel;

        await routed.PipeAsync("docker", ["save", "shop:current"], ["-H", "ssh://deploy@box", "load"], WorkingDir, CancellationToken.None);

        var piped = runner.Invocations.Where(i => i.Arguments.Contains("save") || i.Arguments.Contains("load")).Select(i => i.Arguments).ToList();
        Assert.Equal(["save", "shop:current"], piped[0]);
        Assert.Equal(["-H", $"unix://{Path.Combine(directory, "docker.sock")}", "load"], piped[1]);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
