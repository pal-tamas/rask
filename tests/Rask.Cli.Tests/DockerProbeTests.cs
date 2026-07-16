using System.ComponentModel;
using Rask.Cli;

namespace Rask.Cli.Tests;

public sealed class DockerProbeTests
{
    [Fact]
    public async Task EnsureLocal_true_when_docker_version_exits_zero()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, "Docker version 27.0", string.Empty) };

        var ok = await DockerProbe.EnsureLocalAsync(runner, new StringConsole(), CancellationToken.None);

        Assert.True(ok);
        Assert.Contains(runner.Invocations, i => i is { Captured: true, Arguments: ["--version"] });
    }

    [Fact]
    public async Task EnsureLocal_false_with_install_guidance_when_docker_missing()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(127, string.Empty, "not found") };
        var console = new StringConsole();

        var ok = await DockerProbe.EnsureLocalAsync(runner, console, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("Install Docker", console.ErrorText);
    }

    [Fact]
    public async Task EnsureLocal_treats_a_missing_binary_as_not_installed_instead_of_crashing()
    {
        // Launching an absent `docker` throws Win32Exception rather than returning a non-zero exit.
        var runner = new FakeProcessRunner { CaptureHandler = _ => throw new Win32Exception(2, "No such file or directory") };
        var console = new StringConsole();

        var ok = await DockerProbe.EnsureLocalAsync(runner, console, CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("Install Docker", console.ErrorText);
    }

    [Fact]
    public async Task CanReachHost_probes_the_remote_daemon_over_ssh()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(0, string.Empty, string.Empty) };

        var ok = await DockerProbe.CanReachHostAsync(runner, new StringConsole(), "deploy@box", CancellationToken.None);

        Assert.True(ok);
        Assert.Contains(runner.Invocations, i => i is { Captured: true, Arguments: ["-H", "ssh://deploy@box", "version"] });
    }

    [Fact]
    public async Task CanReachHost_false_with_ssh_and_daemon_guidance_on_failure()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(1, string.Empty, "connection refused") };
        var console = new StringConsole();

        var ok = await DockerProbe.CanReachHostAsync(runner, console, "deploy@box", CancellationToken.None);

        Assert.False(ok);
        Assert.Contains("ssh deploy@box", console.ErrorText);
        Assert.Contains("docker", console.ErrorText, StringComparison.OrdinalIgnoreCase);
    }
}
