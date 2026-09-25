using Rask.Core;

namespace Rask.External;

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
