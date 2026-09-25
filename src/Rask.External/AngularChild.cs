using Rask.Core;

namespace Rask.External;

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
