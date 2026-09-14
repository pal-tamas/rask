using System.IO.Pipelines;

namespace Rask.Server.Transport;

/// <summary>
///     A live session's frames over Server-Sent Events, for a client whose WebSocket never opened.
/// </summary>
/// <remarks>
///     <para>
///         The down half of the fallback: the same frames the socket carries, written to a long-lived HTTP
///         response as <c>data:</c> events. What the client sends comes back up as ordinary POSTs, so this
///         side is write-only — a network that blocks WebSockets still serves a live page, at the cost of
///         one request per interaction.
///     </para>
///     <para>
///         <b>Why frames are written by hand</b> rather than through <c>TypedResults.ServerSentEvents</c>:
///         a frame is already UTF-8 bytes the renderer produced, and the typed helper would take a string
///         per item and serialise it again. This writes the bytes it was handed.
///     </para>
///     <para>
///         <b>Generation</b> is what keeps a stale tab from driving a session it no longer owns. Each stream
///         takes the next number, and an inbound POST carries the one it believes it is talking to; a POST
///         from an older stream is refused rather than applied to the newer one.
///     </para>
/// </remarks>
internal sealed class SseTransport(PipeWriter writer, int generation, CancellationToken lifetime = default)
    : ILiveTransport
{
    private static readonly byte[] DataPrefix = "data: "u8.ToArray();
    private static readonly byte[] FrameEnd = "\n\n"u8.ToArray();
    private static readonly byte[] Newline = "\n"u8.ToArray();
    private static readonly byte[] Heartbeat = ":\n\n"u8.ToArray();
    private static readonly byte[] CloseEventPrefix = "event: close\ndata: "u8.ToArray();

    // One writer at a time. The session serialises its own frames behind the render lock, but the stream's
    // heartbeat is on a timer and knows nothing about that lock.
    private readonly SemaphoreSlim _write = new(1, 1);

    private readonly TaskCompletionSource _completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private volatile bool _closed;

    /// <summary>Which stream this is for the session — see the note on generations above.</summary>
    public int Generation { get; } = generation;

    public bool IsOpen => !_closed;

    /// <summary>
    ///     Cancelled when the stream's own request ends. What a POST's frames are dispatched under: the POST's
    ///     request token belongs to a request that has already been answered by the time a queued handler runs,
    ///     and the server reuses it for whatever that connection carries next.
    /// </summary>
    public CancellationToken Lifetime { get; } = lifetime;

    /// <summary>
    ///     Completes when the stream is finished with, so the request that owns the response body can return
    ///     and release it. Closing, aborting, or the client disconnecting all land here.
    /// </summary>
    public Task Completed => _completed.Task;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> frame, CancellationToken ct)
    {
        if (_closed)
        {
            return;
        }

        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                return;
            }

            WriteData(frame.Span);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _write.Release();
        }
    }

    /// <summary>
    ///     Writes a comment, which is a valid event that carries nothing. It exists to be bytes: a proxy
    ///     that buffers a quiet response, or a client that gives up on an idle one, both need to see the
    ///     connection is alive between renders.
    /// </summary>
    public async Task HeartbeatAsync(CancellationToken ct)
    {
        if (_closed)
        {
            return;
        }

        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                return;
            }

            Write(Heartbeat);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _write.Release();
        }
    }

    /// <summary>
    ///     Announces the ending as its own event, then lets the request end. A WebSocket has a close code for
    ///     this; an HTTP response has only its own end, which is indistinguishable from a dropped link — so
    ///     the reason has to travel as a frame the client can read before the body stops.
    /// </summary>
    public async Task CloseAsync(LiveTransportClose reason, string description, CancellationToken ct)
    {
        if (_closed)
        {
            return;
        }

        await _write.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_closed)
            {
                return;
            }

            Write(CloseEventPrefix);
            Write(System.Text.Encoding.UTF8.GetBytes(description));
            Write(FrameEnd);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or IOException)
        {
            // The client is already gone. Ending is what we were doing anyway.
        }
        finally
        {
            _write.Release();
            Finish();
        }
    }

    public void Abort() => Finish();

    /// <summary>
    ///     Waits for a write that was already under way when the stream was aborted. <see cref="Abort" /> only
    ///     stops new ones; a send that got past its check is still using the response writer, and the request
    ///     that owns that writer must not return underneath it.
    /// </summary>
    public async Task WaitForWritesAsync(TimeSpan timeout)
    {
        if (await _write.WaitAsync(timeout).ConfigureAwait(false))
        {
            _write.Release();
        }
    }

    private void Finish()
    {
        _closed = true;
        _completed.TrySetResult();
    }

    /// <summary>
    ///     Writes one frame as <c>data:</c> lines. A payload carrying a newline would otherwise end the
    ///     event early and leave the rest as a second, malformed one — Rask's frames are compact JSON and
    ///     carry none, and this stays correct for anything that ever does.
    /// </summary>
    private void WriteData(ReadOnlySpan<byte> frame)
    {
        while (true)
        {
            var newline = frame.IndexOf((byte)'\n');
            if (newline < 0)
            {
                Write(DataPrefix);
                Write(frame);
                Write(FrameEnd);
                return;
            }

            Write(DataPrefix);
            Write(frame[..newline]);
            Write(Newline);
            frame = frame[(newline + 1)..];
        }
    }

    private void Write(ReadOnlySpan<byte> bytes)
    {
        var span = writer.GetSpan(bytes.Length);
        bytes.CopyTo(span);
        writer.Advance(bytes.Length);
    }
}
