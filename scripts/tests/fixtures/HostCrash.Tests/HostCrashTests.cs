using Xunit;

namespace HostCrash.Tests;

/// <summary>Tests that pass, so a run that dies still has results to print `Passed!` over.</summary>
public sealed class SurvivorTests
{
    [Fact]
    public void One_survivor_passes() => Assert.True(true);

    [Fact]
    public void Another_survivor_passes() => Assert.True(true);

    [Fact]
    public void A_third_survivor_passes() => Assert.True(true);
}

/// <summary>The one test that takes the host down, the way HOST_CRASH_MODE says; unset, it passes.</summary>
public sealed class HostKillerTests
{
    [Fact]
    public void The_host_dies_the_way_the_mode_says()
    {
        var mode = Environment.GetEnvironmentVariable("HOST_CRASH_MODE") ?? "";

        switch (mode)
        {
            case "overflow":
                Recurse(0);
                break;
            case "failfast":
                Environment.FailFast("HostCrash.Tests: FailFast on purpose");
                break;
            case "exit0":
                Environment.Exit(0);
                break;
            case "thread":
                var thread = new Thread(() => throw new InvalidOperationException("HostCrash.Tests: thrown on a background thread"));
                thread.Start();
                // Not Join: the dying runtime never lets it return, which would turn this row into a hang.
                Thread.Sleep(TimeSpan.FromSeconds(5));
                break;
            case "hang":
                // Bounded, so the test executable cannot outlive the self-test by much if the hang timeout
                // kills its testhost and leaves this process behind (VSTest does, on macOS).
                Thread.Sleep(TimeSpan.FromSeconds(90));
                break;
        }

        Assert.True(true);
    }

    private static int Recurse(int depth) => Recurse(depth + 1) + 1;
}
