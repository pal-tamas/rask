namespace Rask.Outbox.Tests.@event;

// Declared in a namespace whose segment is a C# keyword, and in its own file because a file-scoped
// namespace can't be reopened elsewhere.
//
// This is the shape that used to dead-letter. Roslyn's default display string escapes the keyword
// ("Rask.Outbox.Tests.@event.KeywordEvent") but Type.FullName does not
// ("Rask.Outbox.Tests.event.KeywordEvent"), so a generator that keyed on the display string registered a
// name the runtime never produces: Deserialize returned null, the processor recorded "No registered
// outbox event type", and the message burned an attempt on every poll until it hit MaxAttempts.
//
// The generator registers this assembly's IOutboxEvent types at module load, so these tests exercise the
// real generated registry, not a stand-in.
public sealed record KeywordEvent(int N) : IOutboxEvent;

/// <summary>Thread-safe sink — the handler runs on the outbox processor's background thread.</summary>
public sealed class KeywordRecorder
{
    private readonly List<KeywordEvent> _events = [];

    public IReadOnlyList<KeywordEvent> Events
    {
        get { lock (_events) { return _events.ToArray(); } }
    }

    public void Add(KeywordEvent e)
    {
        lock (_events) { _events.Add(e); }
    }
}

public sealed class KeywordEventHandler(KeywordRecorder recorder) : INotificationHandler<KeywordEvent>
{
    public Task HandleAsync(KeywordEvent notification, CancellationToken cancellationToken)
    {
        recorder.Add(notification);
        return Task.CompletedTask;
    }
}
