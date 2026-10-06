namespace Rask.Background.Tests;

/// <summary>Enqueues a <see cref="RecordJob" /> through the static facade, with nothing injected.</summary>
public sealed record ChainJob(string Value) : IJob;

public sealed class ChainJobHandler : ICommandHandler<ChainJob>
{
    public async Task Handle(ChainJob command) => await Jobs.Enqueue(new RecordJob(command.Value));
}

/// <summary>
/// A job is work in progress like a request or a render: the static facades reach the app from inside its
/// handler, on the processor's thread, with nothing injected.
/// </summary>
[Collection(JobsDbCollection.Name)]
public sealed class JobFacadeTests
{
    [Fact]
    public async Task A_job_handler_can_enqueue_another_job_through_the_facade()
    {
        await using var h = new JobsHarness();
        await h.Queue.Enqueue(new ChainJob("second"), TestContext.Current.CancellationToken);

        await h.RunUntilAsync(() => h.Recorder.Values.Count == 1);

        Assert.Equal(["second"], h.Recorder.Values);
    }
}
