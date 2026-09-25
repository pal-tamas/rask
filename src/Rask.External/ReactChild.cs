using Rask.Core;

namespace Rask.External;

// One child type per runtime, so a React island's children indexer accepts React islands and refuses a Vue one at
// compile time. Each converts from exactly what Component's own children convert from — text, numbers, dates, bool,
// char and Guid, spelled the same invariant way through ChildText — plus its runtime's island base class, and from
// nothing else.
//
// Seven types rather than one generic: a generic struct cannot declare a conversion from its type argument's base, and
// `IslandChild<T> where T : ExternalComponent` would convert from an island of any runtime. A user-defined conversion
// also never lifts through IEnumerable<>, which is why every island that takes children also has indexers over
// IEnumerable<{Runtime}Component?> and IEnumerable<string?>.

/// <summary>A child a React island can render: a React island, text, a number or a date.</summary>
public readonly struct ReactChild
{
    private ReactChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator ReactChild(ReactComponent? island) => new(island);
    public static implicit operator ReactChild(string? text) => new(text);
    public static implicit operator ReactChild(int value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(long value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(double value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(float value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(bool value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(char value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator ReactChild(TimeSpan value) => new(ChildText.Of(value));
}
