using System.Collections.Concurrent;
using System.Globalization;

namespace Rask.Server.Transport;

/// <summary>
///     Which Server-Sent Events stream each session is currently on.
/// </summary>
/// <remarks>
///     <para>
///         A WebSocket needs nothing like this: the connection that receives a frame is the connection that
///         sent it. Over HTTP the two halves are separate requests, so a POST has to say which stream it
///         believes it is talking to, and something has to know the answer.
///     </para>
///     <para>
///         That matters for a tab that reconnects. The new stream takes a higher generation, and frames from
///         the old one — already in flight, or from a tab resumed out of the back/forward cache — are refused
///         instead of being applied to the page the newer stream is driving.
///     </para>
///     <para>
///         One per host, registered by <c>AddRask</c>. Nothing about it is process-wide: two hosts in one
///         process (a test run, an app hosting two Rask apps) each keep their own streams and numbering.
///     </para>
/// </remarks>
internal sealed class StreamRegistry
{
    private readonly ConcurrentDictionary<string, Entry> _streams = new(StringComparer.Ordinal);
    private int _generation;

    /// <summary>The number the next stream takes. Only ever grows, so an older stream is always lower.</summary>
    public int NextGeneration() => Interlocked.Increment(ref _generation);

    /// <summary>Makes <paramref name="transport" /> the session's current stream.</summary>
    public void Set(string sessionId, SseTransport transport) => _streams[sessionId] = new Entry(transport);

    /// <summary>
    ///     Forgets <paramref name="transport" />, unless a newer stream has already replaced it — in which case
    ///     the newer one stays, and this is an old request finishing its cleanup.
    /// </summary>
    public void Remove(string sessionId, SseTransport transport)
    {
        if (_streams.TryGetValue(sessionId, out var entry) && ReferenceEquals(entry.Transport, transport))
        {
            ((ICollection<KeyValuePair<string, Entry>>)_streams).Remove(
                new KeyValuePair<string, Entry>(sessionId, entry));
        }
    }

    /// <summary>
    ///     Whether <paramref name="generation" /> — as the client sent it — names the session's current stream.
    ///     A request that names nothing is refused: the header is how a client says which page it is, and
    ///     guessing on its behalf is what this exists to prevent.
    /// </summary>
    public bool IsCurrent(string sessionId, string? generation) => Current(sessionId, generation) is not null;

    /// <summary>The session's current stream, when <paramref name="generation" /> names it; otherwise null.</summary>
    public Entry? Current(string sessionId, string? generation) =>
        _streams.TryGetValue(sessionId, out var entry)
        && int.TryParse(generation, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
        && value == entry.Transport.Generation
            ? entry
            : null;

    /// <summary>
    ///     Counts <paramref name="frames" /> against the session's one-second window and reports whether they fit
    ///     under <paramref name="maxPerSecond" />. The socket's cap counts messages as they arrive; a POST
    ///     delivers a batch at once, so the batch is what is counted.
    /// </summary>
    public bool TryAdmit(string sessionId, int frames, int maxPerSecond)
    {
        if (!_streams.TryGetValue(sessionId, out var entry))
        {
            return false;
        }

        lock (entry.Gate)
        {
            var now = Environment.TickCount64;
            if (now - entry.WindowStart >= 1000)
            {
                entry.WindowStart = now;
                entry.FramesInWindow = 0;
            }

            entry.FramesInWindow += frames;
            return entry.FramesInWindow <= maxPerSecond;
        }
    }

    /// <summary>Ends the session's stream, telling the client why before the body stops.</summary>
    public void Close(string sessionId, LiveTransportClose reason, string description)
    {
        if (_streams.TryGetValue(sessionId, out var entry))
        {
            // Not awaited: the caller is answering a different request, and the stream's own request is what
            // returns once its close event is written.
            _ = entry.Transport.CloseAsync(reason, description, CancellationToken.None);
        }
    }

    internal sealed class Entry(SseTransport transport)
    {
        internal readonly Lock Gate = new();
        internal long WindowStart = Environment.TickCount64;
        internal int FramesInWindow;

        internal SseTransport Transport { get; } = transport;

        /// <summary>
        ///     One POST at a time per stream. The client already keeps one in flight, but the server cannot rely
        ///     on it: two that overlap — a retry, a tab resumed from the back/forward cache — would race each
        ///     other's dispatch chain and reorder the frames, and each would hold a full body in memory at once.
        /// </summary>
        internal SemaphoreSlim Inbound { get; } = new(1, 1);
    }
}
