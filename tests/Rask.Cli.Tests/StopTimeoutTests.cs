using Rask.Cli.Commands;

namespace Rask.Cli.Tests;

/// <summary>
/// The app's stop budget, <c>"stopTimeout"</c> in <c>.rask/deploy.json</c>: how long a deploy lets the old container
/// finish its running jobs. One number, from which both the deploy's wait and the app's own shutdown budget follow.
/// </summary>
public sealed class StopTimeoutTests
{
    [Theory]
    [InlineData("30m", 1800)]
    [InlineData("90s", 90)]
    [InlineData("1h30m", 5400)]
    [InlineData("2h", 7200)]
    [InlineData(" 45m ", 2700)]
    public void A_stop_timeout_reads_as_hours_minutes_and_seconds(string text, int seconds)
    {
        var read = StopTimeout.TryParse(text, out var value, out var error);

        Assert.True(read, error);
        Assert.Equal(TimeSpan.FromSeconds(seconds), value);
    }

    [Theory]
    [InlineData("30")]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("5s")]
    [InlineData("25h")]
    [InlineData("1.5h")]
    [InlineData("m30")]
    public void A_stop_timeout_that_is_not_a_duration_in_range_is_refused_with_how_to_write_one(string text)
    {
        var read = StopTimeout.TryParse(text, out _, out var error);

        Assert.False(read);
        Assert.Contains("\"30m\"", error, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(1800, "30m")]
    [InlineData(90, "1m30s")]
    [InlineData(5400, "1h30m")]
    [InlineData(20, "20s")]
    public void A_stop_timeout_is_written_back_the_way_people_write_it(int seconds, string expected)
    {
        var text = StopTimeout.Format(TimeSpan.FromSeconds(seconds));

        Assert.Equal(expected, text);
    }

    [Fact]
    public void The_default_stop_budget_is_thirty_minutes()
    {
        var budget = ShutdownBudget.DefaultStopTimeout;

        Assert.Equal(TimeSpan.FromMinutes(30), budget);
    }

    [Fact]
    public void The_app_gives_up_before_the_deploy_kills_it()
    {
        var stop = TimeSpan.FromMinutes(30);

        var host = ShutdownBudget.HostShutdownSeconds(stop);

        Assert.Equal(1800 - ShutdownBudget.HostReserveSeconds, host);
        Assert.True(ShutdownBudget.HostReserveSeconds >= 5, "leave margin for teardown after Host.StopAsync returns");
    }

    [Fact]
    public void The_smallest_stop_budget_still_leaves_the_app_a_budget_of_its_own()
    {
        var host = ShutdownBudget.HostShutdownSeconds(ShutdownBudget.MinimumStopTimeout);

        Assert.True(host >= 5, $"the app would get {host}s");
    }

    [Fact]
    public void The_stop_runs_with_the_budget_and_the_app_is_told_its_share()
    {
        var stop = DockerCommands.BuildStopArguments("deploy@box", "shop-blue", TimeSpan.FromMinutes(45));
        var run = DockerCommands.BuildRunArguments("deploy@box", "shop", "shop.example.com", "green", 8080, [], stopTimeout: TimeSpan.FromMinutes(45));

        Assert.Equal(["-H", "ssh://deploy@box", "stop", "-t", "2700", "shop-blue"], stop);
        Assert.Contains("ASPNETCORE_SHUTDOWNTIMEOUTSECONDS=2695", run);
    }

    [Fact]
    public void The_running_jobs_are_read_from_the_simple_console_log()
    {
        const string logs = "info: Rask.Background.JobProcessor[0]\n      Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m2s)\n"
            + "info: Rask.Background.JobProcessor[0]\n      Stopping: no new job is claimed; waiting for 1 running job(s) to finish: AtPump (3m12s)\n";

        var running = StopProgress.RunningJobs(logs);

        Assert.Equal("1 job: AtPump (3m12s)", running);
    }

    [Fact]
    public void The_running_jobs_are_read_from_the_json_console_log()
    {
        const string logs = """{"EventId":0,"LogLevel":"Information","Category":"Rask.Background.JobProcessor","Message":"Stopping: no new job is claimed; waiting for 2 running job(s) to finish: AtPump (3m12s), Backup (4s)","State":{}}""";

        var running = StopProgress.RunningJobs(logs);

        Assert.Equal("2 jobs: AtPump (3m12s), Backup (4s)", running);
    }

    [Fact]
    public void A_log_without_the_line_says_nothing_about_jobs()
    {
        var running = StopProgress.RunningJobs("info: Microsoft.Hosting.Lifetime[0]\n      Application is shutting down...\n");

        Assert.Null(running);
    }
}
