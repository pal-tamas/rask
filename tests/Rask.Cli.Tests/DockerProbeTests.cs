using System.ComponentModel;
using Rask.Cli;

namespace Rask.Cli.Tests;

public sealed class DockerProbeTests
{
    [Fact]
    public async Task Local_docker_is_ready_when_docker_version_exits_zero()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "Docker version 27.0", string.Empty) };

        var ok = await DockerProbe.EnsureLocalAsync(runner, new StringConsole(), CancellationToken.None);

        Assert.True(ok);
        Assert.Contains(runner.Invocations, i => i is { Captured: true, Arguments: ["--version"] });
    }

    [Fact]
    public async Task Missing_docker_is_not_ready_and_prints_install_guidance()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(127, string.Empty, "not found") };
        var console = new StringConsole();

        var ok = await DockerProbe.EnsureLocalAsync(runner, console, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("Install Docker", console.ErrorText);
    }

    [Fact]
    public async Task A_missing_docker_binary_counts_as_not_installed_instead_of_crashing()
    {
        // Launching an absent `docker` throws Win32Exception rather than returning a non-zero exit.
        var runner = new FakeProcessRunner { CaptureHandler = _ => throw new Win32Exception(2, "No such file or directory") };
        var console = new StringConsole();

        var ok = await DockerProbe.EnsureLocalAsync(runner, console, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("Install Docker", console.ErrorText);
    }

    [Fact]
    public async Task The_install_hint_names_a_command_to_run_not_just_a_page_to_read()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(127, string.Empty, "not found") };
        var console = new StringConsole();

        await DockerProbe.EnsureLocalAsync(runner, console, CancellationToken.None);

        // Whichever platform the tests run on, the hint must be actionable.
        Assert.Contains("docker.com/get-docker", console.ErrorText, StringComparison.Ordinal);
        Assert.Contains(
            new[] { "brew install", "winget install", "get.docker.com | sh" },
            hint => console.ErrorText.Contains(hint, StringComparison.Ordinal));
    }

    // Remote reachability is no longer DockerProbe's job — HostProbe covers it in the same round-trip
    // and can tell "Docker isn't installed" from "you're not in the docker group". See HostProbeTests.
}
