using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

public sealed class DockerTunnelTests
{
    [Fact]
    public void The_forward_is_its_own_connection_and_fails_rather_than_prompting_or_half_working()
    {
        var target = SshTarget.Parse("deploy@box:2222");

        var args = DockerTunnel.BuildArguments(target, "/tmp/rask-x/docker.sock", "/tmp/rask-x/ssh.log");

        Assert.Equal(["-n", "-N", "-T"], args.Take(3));
        Assert.Contains("ExitOnForwardFailure=yes", args);
        Assert.Contains("BatchMode=yes", args);
        Assert.Contains("ControlPath=none", args);
        Assert.Contains("/tmp/rask-x/docker.sock:/var/run/docker.sock", args);
        Assert.Contains("/tmp/rask-x/ssh.log", args);
        Assert.Equal(["-p", "2222", "--", "deploy@box"], args.TakeLast(4));
    }

    [Fact]
    public async Task Calls_to_the_box_go_through_an_open_tunnel_and_the_old_way_once_it_is_closed()
    {
        var runner = new FakeProcessRunner();
        var held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var routed = new TunnelRunner(held);
        var (tunnel, failure) = await DockerTunnel.OpenAsync(held, "deploy@box", new ReadinessPoll { Interval = TimeSpan.Zero, MaxAttempts = 1 }, () => directory, CancellationToken.None);
        routed.Tunnel = tunnel;

        await routed.RunAsync("docker", ["-H", "ssh://deploy@box", "ps"], null, CancellationToken.None);
        await routed.RunAsync("docker", ["-H", "ssh://other@box", "ps"], null, CancellationToken.None);
        await tunnel!.DisposeAsync();
        await routed.RunAsync("docker", ["-H", "ssh://deploy@box", "ps"], null, CancellationToken.None);

        Assert.Null(failure);
        var calls = runner.Invocations.Where(i => i.Arguments is [.., "ps"]).Select(i => i.Arguments[1]).ToList();
        Assert.Equal([$"unix://{Path.Combine(directory, "docker.sock")}", "ssh://other@box", "ssh://deploy@box"], calls);
        Assert.Equal(1, held.TunnelsClosed);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task A_forward_docker_never_answers_through_is_closed_and_said_why()
    {
        var runner = new FakeProcessRunner { CaptureResult = new ProcessResult(1, string.Empty, "Cannot connect to the Docker daemon") };
        var held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var clock = new SteppedClock();

        var (tunnel, failure) = await DockerTunnel.OpenAsync(held, "deploy@box", clock.Poll(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(20)), () => directory, CancellationToken.None);

        Assert.Null(tunnel);
        Assert.Contains("did not answer", failure, StringComparison.Ordinal);
        Assert.Contains("Cannot connect to the Docker daemon", failure, StringComparison.Ordinal);
        Assert.Equal(1, held.TunnelsClosed);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task A_forward_the_box_refuses_ends_the_wait_and_names_the_sshd_setting()
    {
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var probes = 0;
        var runner = new FakeProcessRunner
        {
            // What a box with `AllowTcpForwarding no` does: the connection holds, each forwarded socket is turned down.
            CaptureHandler = _ =>
            {
                probes++;
                File.WriteAllText(DockerTunnel.LogIn(directory), "channel 2: open failed: administratively prohibited: open failed\n");
                return new ProcessResult(1, string.Empty, "error during connect: EOF");
            },
        };
        var held = new HeldSshRunner(runner);
        var clock = new SteppedClock();

        var (tunnel, failure) = await DockerTunnel.OpenAsync(held, "deploy@box", clock.Poll(TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(20)), () => directory, CancellationToken.None);

        Assert.Null(tunnel);
        Assert.Equal(1, probes);
        Assert.Contains("AllowTcpForwarding", failure, StringComparison.Ordinal);
        Assert.Contains("administratively prohibited", failure, StringComparison.Ordinal);
        Assert.Equal(1, held.TunnelsClosed);
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task An_ssh_that_will_not_connect_says_why_in_its_own_words()
    {
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        File.WriteAllText(DockerTunnel.LogIn(directory), "root@box: Permission denied (publickey).\n");
        var runner = new FakeProcessRunner();

        var (tunnel, failure) = await DockerTunnel.OpenAsync(runner, "deploy@box", new ReadinessPoll { Interval = TimeSpan.Zero, MaxAttempts = 1 }, () => directory, CancellationToken.None);

        Assert.Null(tunnel);
        Assert.Equal("ssh would not hold the forward: root@box: Permission denied (publickey).", failure);
        Assert.False(Directory.Exists(directory));
    }
}
