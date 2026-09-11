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

/// <summary>A child a Preact island can render: a Preact island, text, a number or a date.</summary>
public readonly struct PreactChild
{
    private PreactChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator PreactChild(PreactComponent? island) => new(island);
    public static implicit operator PreactChild(string? text) => new(text);
    public static implicit operator PreactChild(int value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(long value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(double value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(float value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(bool value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(char value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator PreactChild(TimeSpan value) => new(ChildText.Of(value));
}

/// <summary>A child a Solid island can render: a Solid island, text, a number or a date.</summary>
public readonly struct SolidChild
{
    private SolidChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator SolidChild(SolidComponent? island) => new(island);
    public static implicit operator SolidChild(string? text) => new(text);
    public static implicit operator SolidChild(int value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(long value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(double value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(float value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(bool value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(char value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator SolidChild(TimeSpan value) => new(ChildText.Of(value));
}

/// <summary>A child a Vue island can render: a Vue island, text, a number or a date.</summary>
public readonly struct VueChild
{
    private VueChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator VueChild(VueComponent? island) => new(island);
    public static implicit operator VueChild(string? text) => new(text);
    public static implicit operator VueChild(int value) => new(ChildText.Of(value));
    public static implicit operator VueChild(long value) => new(ChildText.Of(value));
    public static implicit operator VueChild(double value) => new(ChildText.Of(value));
    public static implicit operator VueChild(float value) => new(ChildText.Of(value));
    public static implicit operator VueChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator VueChild(bool value) => new(ChildText.Of(value));
    public static implicit operator VueChild(char value) => new(ChildText.Of(value));
    public static implicit operator VueChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator VueChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator VueChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator VueChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator VueChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator VueChild(TimeSpan value) => new(ChildText.Of(value));
}

/// <summary>A child a Svelte island can render: a Svelte island, text, a number or a date.</summary>
public readonly struct SvelteChild
{
    private SvelteChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator SvelteChild(SvelteComponent? island) => new(island);
    public static implicit operator SvelteChild(string? text) => new(text);
    public static implicit operator SvelteChild(int value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(long value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(double value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(float value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(bool value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(char value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator SvelteChild(TimeSpan value) => new(ChildText.Of(value));
}

/// <summary>A child a Lit island can render: a Lit island, text, a number or a date.</summary>
public readonly struct LitChild
{
    private LitChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator LitChild(LitComponent? island) => new(island);
    public static implicit operator LitChild(string? text) => new(text);
    public static implicit operator LitChild(int value) => new(ChildText.Of(value));
    public static implicit operator LitChild(long value) => new(ChildText.Of(value));
    public static implicit operator LitChild(double value) => new(ChildText.Of(value));
    public static implicit operator LitChild(float value) => new(ChildText.Of(value));
    public static implicit operator LitChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator LitChild(bool value) => new(ChildText.Of(value));
    public static implicit operator LitChild(char value) => new(ChildText.Of(value));
    public static implicit operator LitChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator LitChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator LitChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator LitChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator LitChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator LitChild(TimeSpan value) => new(ChildText.Of(value));
}

/// <summary>A child an Angular island can render: an Angular island, text, a number or a date.</summary>
public readonly struct AngularChild
{
    private AngularChild(object? value) => Value = value;

    /// <summary>The island, or the text the child renders as; null renders nothing.</summary>
    internal object? Value { get; }

    public static implicit operator AngularChild(AngularComponent? island) => new(island);
    public static implicit operator AngularChild(string? text) => new(text);
    public static implicit operator AngularChild(int value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(long value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(double value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(float value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(decimal value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(bool value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(char value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(Guid value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(DateOnly value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(TimeOnly value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(DateTime value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(DateTimeOffset value) => new(ChildText.Of(value));
    public static implicit operator AngularChild(TimeSpan value) => new(ChildText.Of(value));
}
