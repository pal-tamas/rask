using System.Threading.Channels;

namespace Rask.Logging;

/// <summary>
/// The bounded hand-off between the logging call site and the background writer.
/// <para>
/// It is bounded and lossy on purpose. A log call happens on whatever thread is serving a request, and the
/// one thing it must never do is wait for a disk write — so when the buffer is full the entry is
/// <b>dropped</b> rather than queued or blocked on, and counted on <c>rask.logs.dropped</c>. Unbounded
/// buffering would trade a visible drop count for an invisible memory leak under exactly the log storm that
/// causes it.
/// </para>
/// </summary>
internal sealed class LogChannel
{
    private readonly Channel<LogRecord> _channel;

    public LogChannel(RaskLoggingOptions options, LogMetrics metrics)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(metrics);

        _channel = Channel.CreateBounded<LogRecord>(
            new BoundedChannelOptions(options.QueueCapacity)
            {
                // DropWrite: the newest entry loses, so a storm can't evict the older entries that explain
                // how it started. TryWrite reports success in every Drop* mode, so the drop is counted
                // through this callback rather than by inspecting its return value.
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = false,
            },
            _ => metrics.Dropped());
    }

    /// <summary>How many entries are buffered, waiting for the next flush.</summary>
    public int Count => _channel.Reader.Count;

    /// <summary>Buffers an entry. Never blocks, never throws; a full buffer drops it.</summary>
    public void Write(LogRecord record) => _channel.Writer.TryWrite(record);

    /// <summary>Takes the next buffered entry, if any.</summary>
    public bool TryRead(out LogRecord record) => _channel.Reader.TryRead(out record!);

    /// <summary>Refuses further entries. Called once the writer has finished its shutdown drain.</summary>
    public void Complete() => _channel.Writer.TryComplete();
}
