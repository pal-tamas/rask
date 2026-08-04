namespace Rask.Example.Shop.Features.Orders;

/// <summary>A background job — enqueue it with <see cref="IJobQueue"/> and it runs off the request thread.</summary>
public sealed record PurgeStaleCarts : IJob;

public sealed class PurgeStaleCartsHandler : ICommandHandler<PurgeStaleCarts>
{
    public Task HandleAsync(PurgeStaleCarts job, CancellationToken cancellationToken)
    {
        // TODO: do the work.
        return Task.CompletedTask;
    }
}
