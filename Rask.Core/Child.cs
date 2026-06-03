using System.Globalization;
using Rask.Core.Components;

namespace Rask.Core;

public readonly struct Child
{
    public Child(Component component) => Component = component;
    public Child(string text) => Component = new Text { Value = text };

    public Component Component { get; }

    public static implicit operator Child(Component component) => new(component);
    public static implicit operator Child(string text) => new(text);

    // Value types auto-stringify, so a call site can write `Td()[f.TemperatureC]` instead of
    // `Td()[f.TemperatureC.ToString()]`. IFormattable values are rendered with InvariantCulture so
    // the HTML stays locale-independent and byte-stable for the diff codec — matching the
    // formatting convention in Forms/BindingHelpers.FormatValue and RouteValueParser. Narrower
    // integer types (byte, short, …) reach the `int` operator via a standard widening conversion;
    // `char` has its own operator so it renders the character, not its numeric code point.
    public static implicit operator Child(int value) => Format(value);
    public static implicit operator Child(long value) => Format(value);
    public static implicit operator Child(double value) => Format(value);
    public static implicit operator Child(float value) => Format(value);
    public static implicit operator Child(decimal value) => Format(value);
    public static implicit operator Child(bool value) => new(value ? "True" : "False");
    public static implicit operator Child(char value) => new(value.ToString());
    public static implicit operator Child(Guid value) => new(value.ToString());
    public static implicit operator Child(DateOnly value) => Format(value);
    public static implicit operator Child(TimeOnly value) => Format(value);
    public static implicit operator Child(DateTime value) => Format(value);
    public static implicit operator Child(DateTimeOffset value) => Format(value);
    public static implicit operator Child(TimeSpan value) => Format(value);

    private static Child Format<T>(T value)
        where T : IFormattable =>
        new(value.ToString(null, CultureInfo.InvariantCulture));
}
