using System.ComponentModel;

namespace Rask.Cli.Tests;

/// <summary>
///     Whether the local docker CLI can build a Dockerfile that uses <c>RUN --mount</c>: buildx, or Podman's shim.
/// </summary>
public sealed class BuildxProbeTests
{
    [Fact]
    public async Task A_docker_cli_whose_buildx_answers_is_ready_to_build()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "github.com/docker/buildx v0.17.1", string.Empty) };

        var status = await BuildxProbe.CheckAsync(runner, "Docker version 27.3.1", CancellationToken.None);

        Assert.Equal(BuildxStatus.Present, status);
        Assert.Contains(runner.Invocations, i => i is { FileName: "docker", Arguments: ["buildx", "version"] });
    }

    [Fact]
    public async Task A_docker_cli_whose_buildx_is_not_a_command_is_missing_it()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(1, string.Empty, "docker: 'buildx' is not a docker command.") };

        var status = await BuildxProbe.CheckAsync(runner, "Docker version 27.3.1, build ce12230", CancellationToken.None);

        Assert.Equal(BuildxStatus.Missing, status);
    }

    [Fact]
    public async Task A_docker_that_cannot_be_launched_for_buildx_is_missing_it_rather_than_crashing()
    {
        var runner = new FakeProcessRunner { CaptureHandler = _ => throw new Win32Exception(2, "No such file or directory") };

        var status = await BuildxProbe.CheckAsync(runner, "Docker version 27.3.1", CancellationToken.None);

        Assert.Equal(BuildxStatus.Missing, status);
    }

    [Theory]
    [InlineData("podman version 5.2.0")]
    [InlineData("Podman version 4.9.3")]
    public async Task Podmans_docker_shim_needs_no_buildx_and_is_not_asked_for_it(string dockerVersion)
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(1, string.Empty, "unknown command") };

        var status = await BuildxProbe.CheckAsync(runner, dockerVersion, CancellationToken.None);

        Assert.Equal(BuildxStatus.Podman, status);
        Assert.Empty(runner.Invocations);
    }

    [Theory]
    [InlineData(true, false, "brew install docker-buildx")]
    [InlineData(false, true, "apt-get install docker-buildx-plugin")]
    [InlineData(false, false, "https://docs.docker.com/go/buildx/")]
    public void The_fix_names_the_install_command_for_the_platform(bool macOS, bool debian, string expected)
    {
        var platform = (macOS, debian);

        var hint = BuildxProbe.FixHint(platform.macOS, platform.debian);

        Assert.Contains(expected, hint, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_deploy_preflight_says_what_is_wrong_and_how_to_fix_it()
    {
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args => args is ["--version"]
                ? new ProcessResult(0, "Docker version 27.3.1", string.Empty)
                : new ProcessResult(1, string.Empty, "docker: 'buildx' is not a docker command."),
        };
        var console = new StringConsole();

        var ok = await DockerProbe.EnsureBuildxAsync(runner, console, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("no buildx plugin", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(BuildxProbe.FixHint(), console.ErrorText, StringComparison.Ordinal);
        Assert.Contains("Nothing was sent to the host", console.ErrorText, StringComparison.Ordinal);
    }
}
