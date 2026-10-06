using System.Globalization;

namespace Rask;

/// <summary>A CSS length, written the way it is said: <c>12.Px</c>, <c>1.5.Rem</c>, <c>100.Vh</c>.</summary>
/// <remarks>Made by the <see cref="Units" /> literals and taken wherever a CSS property takes a length.</remarks>
public readonly record struct Length
{
    private readonly string? _text;

    internal Length(double value, string unit) => _text = value.ToString(CultureInfo.InvariantCulture) + unit;

    /// <summary>The length as CSS writes it: <c>12px</c>.</summary>
    public override string ToString() => _text ?? "0";
}
