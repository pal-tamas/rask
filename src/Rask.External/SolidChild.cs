using Rask.Core;

namespace Rask.External;

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
