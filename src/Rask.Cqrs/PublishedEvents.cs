namespace Rask.Cqrs;

/// <summary>
///     The events of one kind a test published, narrowed by the steps taken so far and waiting for the count that
///     asserts: <c>events.Published&lt;OrderPlaced&gt;().Where(e =&gt; e.Id == id).Once()</c>.
/// </summary>
/// <typeparam name="TEvent">The kind of event asked about.</typeparam>
/// <remarks>
///     The counting steps (<see cref="Once" />, <see cref="Never" />, <see cref="Times" />) are what check, and they throw
///     a <see cref="PublishedEventsException" /> naming every event that <em>was</em> published — the useful half of a
///     failing expectation is what happened instead.
/// </remarks>
public sealed class PublishedEvents<TEvent>
    where TEvent : IEvent
{
    private readonly IReadOnlyList<IEvent> _published;
    private readonly string _narrowing;

    internal PublishedEvents(IReadOnlyList<IEvent> published)
        : this(published, [.. published.OfType<TEvent>()], "")
    {
    }

    private PublishedEvents(IReadOnlyList<IEvent> published, IReadOnlyList<TEvent> matching, string narrowing)
    {
        _published = published;
        All = matching;
        _narrowing = narrowing;
    }

    /// <summary>The events that match every step taken so far, in the order they were published.</summary>
    public IReadOnlyList<TEvent> All { get; }

    /// <summary>How many match every step taken so far.</summary>
    public int Count => All.Count;

    /// <summary>Narrows to the events <paramref name="predicate" /> accepts: <c>.Where(e =&gt; e.DriverId == id)</c>.</summary>
    /// <param name="predicate">What to keep.</param>
    public PublishedEvents<TEvent> Where(Func<TEvent, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        return new PublishedEvents<TEvent>(_published, [.. All.Where(predicate)], "that matched");
    }

    /// <summary>Asserts exactly one matched.</summary>
    /// <exception cref="PublishedEventsException">Not exactly one matched.</exception>
    public void Once() => Times(1);

    /// <summary>Asserts none matched.</summary>
    /// <exception cref="PublishedEventsException">One or more matched.</exception>
    public void Never() => Times(0);

    /// <summary>Asserts exactly <paramref name="count" /> matched.</summary>
    /// <param name="count">How many are expected.</param>
    /// <exception cref="PublishedEventsException">A different number matched.</exception>
    public void Times(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        if (All.Count != count)
        {
            throw new PublishedEventsException(Explain(count));
        }
    }

    /// <summary>The one event that matched, to assert on what the steps do not cover. Throws unless exactly one matched.</summary>
    /// <exception cref="PublishedEventsException">Not exactly one matched.</exception>
    public TEvent Only()
    {
        Once();
        return All[0];
    }

    private string Explain(int expected)
    {
        var what = _narrowing.Length == 0 ? typeof(TEvent).Name : $"{typeof(TEvent).Name} {_narrowing}";
        var found = All.Count switch
        {
            0 => "none was",
            1 => "one was",
            _ => $"{Say(All.Count)} were",
        };
        var head = $"Expected {Say(expected)} {what}; {found} published.";
        return _published.Count == 0
            ? $"{head} Nothing was published at all."
            : $"{head} Published: {string.Join("; ", _published)}.";

        static string Say(int n) => n switch
        {
            0 => "no",
            1 => "one",
            _ => n.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }
}
