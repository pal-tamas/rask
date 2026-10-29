using Microsoft.Playwright;

namespace Rask.Server.E2E.Tests.Infrastructure;

/// <summary>
///     The page's socket saying hello: the frame that tells a journey the socket is up, and that what the load
///     itself sends has been sent.
/// </summary>
/// <remarks>
///     Neither the first page's text nor the hooks' bundle having run says so: both come before the socket opens,
///     and on a loaded machine the hello then lands in what a journey counts, or the page's clock is stopped while the
///     socket is still opening — its open deadline is page time, so a journey's own <c>RunFor</c> then gives up on it
///     and the page goes over HTTP, where a socket's counter sees nothing. Watch it after any counter of the same
///     socket's frames, so the hello is in that count by the time this says it went.
/// </remarks>
internal sealed class SocketHello
{
    // Bounds a hang, never the work.
    private static readonly TimeSpan Hang = TimeSpan.FromSeconds(30);

    private readonly TaskCompletionSource _sent = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Starts watching every socket <paramref name="page" /> opens. Call before the page loads.</summary>
    public static SocketHello Watch(IPage page)
    {
        var hello = new SocketHello();
        page.WebSocket += (_, socket) => socket.FrameSent += (_, frame) =>
        {
            if (frame.Text?.StartsWith("{\"type\":\"hello\"", StringComparison.Ordinal) == true)
            {
                hello._sent.TrySetResult();
            }
        };

        return hello;
    }

    /// <summary>Completes once the page's socket has said hello.</summary>
    public Task SentAsync() => _sent.Task.WaitAsync(Hang, TestContext.Current.CancellationToken);
}
