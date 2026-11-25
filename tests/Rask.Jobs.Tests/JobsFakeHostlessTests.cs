using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Rask.Data;

namespace Rask.Background.Tests;

/// <summary>
/// A test with a <see cref="TestDatabase" /> and no host: the database's services are the app it has, so a fake's
/// <c>Run()</c> reaches the job's handler through them.
/// </summary>
public sealed class JobsFakeHostlessTests
{
    [Fact]
    public async Task Run_reaches_the_handler_through_the_test_database_when_no_host_started()
    {
        var recorder = new Recorder();
        await using var db = await TestDatabase.Start(
            o => o.UseSqlite("Data Source=:memory:"),
            services: s => s.AddSingleton(recorder),
            cancellationToken: TestContext.Current.CancellationToken);
        using var jobs = Jobs.Fake();
        await Jobs.Enqueue(new RecordJob("receipt"), TestContext.Current.CancellationToken);

        await jobs.Run(TestContext.Current.CancellationToken);

        Assert.Equal(["receipt"], recorder.Values);
    }

    [Fact]
    public async Task An_awaited_job_runs_through_the_test_database_when_no_host_started()
    {
        var recorder = new Recorder();
        await using var db = await TestDatabase.Start(
            o => o.UseSqlite("Data Source=:memory:"),
            services: s => s.AddSingleton(recorder),
            cancellationToken: TestContext.Current.CancellationToken);
        using var jobs = Jobs.Fake();

        await Jobs.Run(new RecordJob("now"), TestContext.Current.CancellationToken);

        Assert.Equal(["now"], recorder.Values);
    }
}
