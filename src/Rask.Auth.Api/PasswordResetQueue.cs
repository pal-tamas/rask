using System.Threading.Channels;

namespace Rask.Auth;

/// <summary>
///     Reset requests waiting for <see cref="PasswordResetSender{TUser, TKey}" /> to look their address up and queue its email:
///     a request hands its address over and answers at once, the same way for an address with an account as for one without,
///     so nothing in the answer — its time included — says which it was.
/// </summary>
/// <remarks>
///     In memory and bounded: a request finding it full (or with no sender running) does the work itself, held to
///     <see cref="AuthOptions.ResetAnswerFloor" />. A restart drops what was waiting; whoever asked asks again.
/// </remarks>
internal sealed class PasswordResetQueue
{
    private readonly Channel<PasswordResetRequest> _requests =
        Channel.CreateBounded<PasswordResetRequest>(new BoundedChannelOptions(1_000) { SingleReader = true });

    private int _pending;
    private TaskCompletionSource _idle = Completed();

    /// <summary>Whether a sender is reading: until it starts, a request does its own work.</summary>
    public bool IsRunning { get; set; }

    internal ChannelReader<PasswordResetRequest> Reader => _requests.Reader;

    /// <summary>Hands <paramref name="request" /> to the sender; false when it is not running or has no room.</summary>
    public bool TryQueue(PasswordResetRequest request)
    {
        if (!IsRunning)
        {
            return false;
        }

        lock (_requests)
        {
            if (!_requests.Writer.TryWrite(request))
            {
                return false;
            }

            if (_pending++ == 0)
            {
                _idle = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            }
        }

        return true;
    }

    /// <summary>Done with one: the sender looked it up and queued its email, or gave up on it.</summary>
    public void Done()
    {
        lock (_requests)
        {
            if (--_pending == 0)
            {
                _idle.TrySetResult();
            }
        }
    }

    /// <summary>Completes once nothing is waiting: what a test awaits before it reads the outbox.</summary>
    public Task WhenIdle()
    {
        lock (_requests)
        {
            return _idle.Task;
        }
    }

    private static TaskCompletionSource Completed()
    {
        var done = new TaskCompletionSource();
        done.SetResult();
        return done;
    }
}
