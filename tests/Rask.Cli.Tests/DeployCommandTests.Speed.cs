using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// Where a deploy's time goes when nothing is wrong: one ssh login for every docker step, and readiness waits that ask
/// at once and then every second — never a fixed pause before the first ask.
/// </summary>
public sealed partial class DeployCommandTests
{
    private static readonly string[] DomainDeploy = ["--host", "deploy@box", "--domain", "demo.example.com", "--app", "demo"];

    [Fact]
    public async Task One_ssh_connection_carries_every_docker_step_of_a_deploy()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var command = CreateSharing(fs, runner, held, new StringConsole(), directory);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        var socket = Path.Combine(directory, "docker.sock");
        var forward = Assert.Single(runner.Invocations, i => i.FileName == "ssh" && i.Arguments.Contains("-L"));
        Assert.Contains($"{socket}:{DockerTunnel.RemoteSocket}", forward.Arguments);
        var remote = runner.Invocations.Where(i => i.FileName == "docker" && i.Arguments is ["-H", ..]).ToList();
        Assert.True(remote.Count > 20, $"a domain deploy makes its many docker steps ({remote.Count}) over the tunnel");
        Assert.All(remote, i => Assert.Equal($"unix://{socket}", i.Arguments[1]));
        Assert.Equal(1, held.TunnelsOpened);
        Assert.Equal(1, held.TunnelsClosed);
        Assert.False(Directory.Exists(directory), "the forwarded socket's directory is removed with the tunnel");
    }

    [Theory]
    [InlineData("--no-setup-host", 1)]
    [InlineData("--no-health-check", 0)]
    public async Task With_no_setup_host_the_connection_opens_while_the_box_is_probed(string flag, int openWhileProbing)
    {
        var fs = new FakeFileSystem();
        HeldSshRunner? held = null;
        var seenAtProbe = -1;
        var captures = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n");
        var runner = new FakeProcessRunner
        {
            CaptureHandler = args =>
            {
                if (IsHostProbe(args))
                {
                    seenAtProbe = held!.TunnelsOpened;
                }

                return captures(args);
            },
        };
        held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var command = CreateSharing(fs, runner, held, new StringConsole(), directory);

        var exit = await command.ExecuteAsync([.. DomainDeploy, flag], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(openWhileProbing, seenAtProbe);
        Assert.Equal(1, held.TunnelsOpened);
        Assert.Equal(1, held.TunnelsClosed);
    }

    [Fact]
    public async Task A_box_that_refuses_the_forward_is_deployed_with_a_connection_per_step()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n") };
        var console = new StringConsole();
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var command = CreateSharing(fs, runner, runner, console, directory);

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Contains("Could not keep one SSH connection open", console.OutText, StringComparison.Ordinal);
        var remote = runner.Invocations.Where(i => i.FileName == "docker" && i.Arguments is ["-H", ..]).ToList();
        Assert.All(remote, i => Assert.Equal("ssh://deploy@box", i.Arguments[1]));
        Assert.False(Directory.Exists(directory));
    }

    [Fact]
    public async Task Status_reads_the_box_without_opening_a_tunnel()
    {
        var fs = new FakeFileSystem();
        var runner = new FakeProcessRunner { CaptureHandler = Captures() };
        var held = new HeldSshRunner(runner);
        var directory = Directory.CreateTempSubdirectory("rask-tunnel-test-").FullName;
        var command = CreateSharing(fs, runner, held, new StringConsole(), directory);

        var exit = await command.ExecuteAsync(["status", "--host", "deploy@box", "--app", "demo"], CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(0, held.TunnelsOpened);
        Directory.Delete(directory);
    }

    [Fact]
    public async Task The_first_health_probe_goes_out_at_once_and_a_miss_waits_one_second()
    {
        var fs = new FakeFileSystem();
        var probes = 0;
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"),
            RunHandler = args => IsHealthProbeOf(args, "demo-green") && Interlocked.Increment(ref probes) < 3 ? 7 : 0,
        };
        var clock = new SteppedClock();
        var console = new StringConsole();
        var command = CreateTimed(fs, runner, console, clock, TimeSpan.FromSeconds(60));

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(0, exit);
        Assert.Equal(3, probes);
        Assert.Equal([TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1)], clock.Pauses);
        Assert.Contains("demo-green answered /health after 2s", console.OutText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_health_check_that_never_answers_is_given_up_at_its_deadline()
    {
        var fs = new FakeFileSystem();
        var probes = 0;
        var runner = new FakeProcessRunner
        {
            CaptureHandler = Captures("demo-blue\tdemo\tdemo.example.com\tblue\n"),
            RunHandler = args => IsHealthProbeOf(args, "demo-green") && Interlocked.Increment(ref probes) > 0 ? 7 : 0,
        };
        var clock = new SteppedClock();
        var command = CreateTimed(fs, runner, new StringConsole(), clock, TimeSpan.FromSeconds(10));

        var exit = await command.ExecuteAsync(DomainDeploy, CancellationToken.None);

        Assert.Equal(1, exit);
        Assert.Equal(11, probes);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Elapsed);
        Assert.Contains(runner.Invocations, i => i.Arguments is ["-H", "ssh://deploy@box", "rm", "-f", "demo-green"]);
    }

    private static bool IsHealthProbeOf(IReadOnlyList<string> args, string container) =>
        args.Contains(DeployCommand.CurlImage) && args.Contains($"container:{container}");

    private static DeployCommand CreateSharing(FakeFileSystem fs, FakeProcessRunner runner, IProcessRunner process, StringConsole console, string directory)
    {
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        FakeProxy.Wire(runner, fs);
        return new DeployCommand(console, fs, process, WorkingDir)
        {
            ReadinessDelay = TimeSpan.Zero,
            ReadinessAttempts = 1,
            PreStopDrainDelay = TimeSpan.Zero,
            LockPollDelay = TimeSpan.Zero,
            ShareSshConnection = true,
            CreateTunnelDirectory = () => directory,
        };
    }

    private static DeployCommand CreateTimed(FakeFileSystem fs, FakeProcessRunner runner, StringConsole console, SteppedClock clock, TimeSpan readinessTimeout)
    {
        fs.Seed("/proj/Dockerfile", "FROM scratch");
        FakeProxy.Wire(runner, fs);
        return new DeployCommand(console, fs, runner, WorkingDir)
        {
            ReadinessDelay = TimeSpan.FromSeconds(1),
            ReadinessTimeout = readinessTimeout,
            ReadinessSleep = clock.Pause,
            Clock = clock,
            PreStopDrainDelay = TimeSpan.Zero,
            LockPollDelay = TimeSpan.Zero,
            StopReportDelay = TimeSpan.Zero,
        };
    }
}
