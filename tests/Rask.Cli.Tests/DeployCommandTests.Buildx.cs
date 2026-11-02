namespace Rask.Cli.Tests;

/// <summary>
/// Every scaffolded Dockerfile uses <c>RUN --mount=type=cache</c>, which a docker CLI without buildx cannot build. The
/// deploy says so before it touches the host, instead of failing mid-build in the legacy builder's words.
/// </summary>
public sealed partial class DeployCommandTests
{
    private static Func<IReadOnlyList<string>, ProcessResult> WithoutBuildx(string dockerVersion = "Docker version 27.3.1") => args =>
        args is ["buildx", "version"] ? new ProcessResult(1, string.Empty, "docker: 'buildx' is not a docker command.")
        : args is ["--version"] ? new ProcessResult(0, dockerVersion, string.Empty)
        : Captures()(args);

    [Fact]
    public async Task A_docker_cli_without_buildx_stops_the_deploy_before_the_host_is_touched()
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithoutBuildx() };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "shop"], CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Contains("no buildx plugin", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("RUN --mount", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(BuildxProbe.DocsReference, console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => IsHostProbe(i.Arguments) || i.Arguments.Contains("build"));
    }

    [Fact]
    public async Task A_docker_cli_with_buildx_deploys_as_before()
    {
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var command = Create(new FakeFileSystem(), runner, new StringConsole());

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains(runner.Invocations, i => i is { Captured: true, Arguments: ["buildx", "version"] });
    }

    [Fact]
    public async Task Podmans_docker_shim_deploys_without_being_asked_for_buildx()
    {
        var runner = new FakeProcessRunner { CaptureHandler = WithoutBuildx("podman version 5.2.0") };
        var console = new StringConsole();
        var command = Create(new FakeFileSystem(), runner, console);

        var exit = await command.ExecuteAsync(["--host", "deploy@box", "--app", "shop"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.DoesNotContain("no buildx plugin", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments is ["buildx", "version"]);
    }

    [Fact]
    public async Task Status_builds_nothing_so_a_missing_buildx_does_not_stop_it()
    {
        var fs = new FakeFileSystem();
        fs.Seed("/proj/.rask/deploy.json", """{"host":"deploy@box","name":"shop"}""");
        var runner = new FakeProcessRunner { CaptureHandler = WithoutBuildx() };
        var console = new StringConsole();
        var command = Create(fs, runner, console);

        await command.ExecuteAsync(["status"], CancellationToken.None);

        Assert.DoesNotContain("no buildx plugin", console.ErrorText, StringComparison.Ordinal);
        Assert.DoesNotContain(runner.Invocations, i => i.Arguments is ["buildx", "version"]);
    }
}
