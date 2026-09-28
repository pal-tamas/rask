using Rask.Core;

namespace Rask.External;

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
