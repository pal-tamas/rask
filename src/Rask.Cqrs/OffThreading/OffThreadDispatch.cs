using Microsoft.Extensions.DependencyInjection;

namespace Rask.Cqrs;

// The page's side of [OffThread]: the transport the installed invokers hand a message to (OffThreadInvokers).
//
// The checks the page's own pipeline would have made are made here, because the message leaves before that pipeline
// runs and the worker cannot make them: it has no signed-in user and none of the app's validators. Then the message is
// encoded, sent, and its answer decoded — or the call is cancelled.
//
// Cancelling answers the caller at once and asks the worker to cancel Current.Cancellation. A handler that never looks
// at it (a synchronous Xlsx.Read) cannot hear that, so if the worker has not answered within the grace it is restarted:
// a page that asked to stop must not keep a core busy, and keep the next request waiting, for work nobody wants.
internal sealed class OffThreadDispatch(IServiceProvider services, IOffThreadChannel channel, TimeSpan grace) : IRemoteDispatch
{
    internal static readonly TimeSpan DefaultGrace = TimeSpan.FromSeconds(1);

    private readonly Lock _gate = new();
    private int _next;

    // The cancelled calls whose handlers have not yet stopped. A message sent meanwhile would queue behind one of
    // them in a worker that is about to be restarted, and fail with it; it waits instead — at most the grace — and
    // goes to whichever worker is left.
    private Task _settling = Task.CompletedTask;

    internal IOffThreadChannel Channel => channel;

    public async Task<TResult> Send<TResult>(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var answer = await Call(contract, message, cancellationToken).ConfigureAwait(false);
        return OffThreadAnswer.Read<TResult>(contract, answer);
    }

    public async Task Send(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var answer = await Call(contract, message, cancellationToken).ConfigureAwait(false);
        OffThreadAnswer.Read<object?>(contract, answer);
    }

    // [OffThread] carries a query or a command. An event is published where it happened.
    public Task Publish(RemoteContract contract, object e, CancellationToken cancellationToken) =>
        throw new NotSupportedException("An event is published where it happened; [OffThread] carries queries and commands.");

    private async Task<byte[]> Call(RemoteContract contract, object message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();
        await Admit(contract, message).ConfigureAwait(false);

        var frame = await OffThreadRequest.Write(contract, message, cancellationToken).ConfigureAwait(false);
        await Settled().WaitAsync(cancellationToken).ConfigureAwait(false);
        var id = Interlocked.Increment(ref _next);
        var call = channel.Call(id, frame);

        // A call the caller stopped waiting for still settles, and its fault must not go unobserved.
        _ = call.ContinueWith(
            static settled => _ = settled.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        // The cancellation is heard where the wait ends, not through a registration of its own: WaitAsync's runs first,
        // and the await it ends would dispose a second one before it ran.
        try
        {
            return await call.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Stop(id, call);
            throw;
        }
        catch (Exception lost)
        {
            throw new RemoteDispatchException(
                $"'{contract.Name}' did not finish: the worker running it stopped before it answered.", lost)
            {
                MessageName = contract.Name,
            };
        }
    }

    // What AuthorizationBehavior and ValidationBehavior would have said on the page, said before the message leaves it.
    private async Task Admit(RemoteContract contract, object message)
    {
        if (CqrsRegistry.FindAuthorization(contract.MessageType) is { } declared
            && services.GetService<IDispatchPrincipal>()?.Current is { } user
            && await RequestAccess.Refusal(
                    user, contract.MessageType.Name, requiresAuthentication: true, declared.RoleSets, declared.Policies, services)
                .ConfigureAwait(false) is { } refusal)
        {
            throw refusal;
        }

        if (services.GetService<IRemoteRequestValidator>() is { } validator)
        {
            var errors = await validator.Validate(message).ConfigureAwait(false);
            if (errors.Count > 0)
            {
                throw new RaskValidationException(errors);
            }
        }
    }

    private Task Settled()
    {
        lock (_gate)
        {
            return _settling;
        }
    }

    private void Stop(int id, Task<byte[]> call)
    {
        channel.Cancel(id);
        var stopping = RestartUnlessStopped(call);
        lock (_gate)
        {
            _settling = _settling.IsCompleted ? stopping : Task.WhenAll(_settling, stopping);
        }
    }

    private async Task RestartUnlessStopped(Task<byte[]> call)
    {
        using var timer = new CancellationTokenSource();
        var settled = await Task.WhenAny(call, Task.Delay(grace, timer.Token)).ConfigureAwait(false);
        await timer.CancelAsync().ConfigureAwait(false);
        if (settled != call)
        {
            channel.Restart();
        }
    }
}
