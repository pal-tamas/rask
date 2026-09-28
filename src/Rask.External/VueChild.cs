using Rask.Core;

namespace Rask.External;

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
