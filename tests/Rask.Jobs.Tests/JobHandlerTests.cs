namespace Rask.Background.Tests;

/// <summary>A job handled through the job-named interface, its <c>Handle</c> public.</summary>
public sealed record JobHandlerJob(string Value) : IJob;

public sealed class JobHandlerJobHandler(Recorder recorder) : IJobHandler<JobHandlerJob>
{
    public Task Handle(JobHandlerJob job)
    {
        recorder.Add(job.Value);
        return Task.CompletedTask;
    }
}

/// <summary>A job handled through the job-named interface, its <c>Handle</c> implemented explicitly.</summary>
public sealed record ExplicitJobHandlerJob(string Value) : IJob;

public sealed class ExplicitJobHandlerJobHandler(Recorder recorder) : IJobHandler<ExplicitJobHandlerJob>
{
    Task IJobHandler<ExplicitJobHandlerJob>.Handle(ExplicitJobHandlerJob job)
    {
        recorder.Add(job.Value);
        return Task.CompletedTask;
    }
}

/// <summary>
/// <c>IJobHandler&lt;TJob&gt;</c> is found by the build and run by the processor exactly like the
/// <c>ICommandHandler&lt;TJob&gt;</c> the other tests here use.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobHandlerTests
{
    [Fact]
    public async Task A_job_handler_runs_the_job_it_handles()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new JobHandlerJob("ran"), TestContext.Current.CancellationToken);

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal(["ran"], h.Recorder.Values);
    }

    [Fact]
    public async Task A_job_handler_that_implements_handle_explicitly_runs_the_job_too()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ExplicitJobHandlerJob("explicit"), TestContext.Current.CancellationToken);

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal(["explicit"], h.Recorder.Values);
    }
}
