using System.Globalization;

namespace Rask;

/// <summary>A CSS percentage: <c>50.Percent</c>.</summary>
/// <remarks>Its own type because not every property that takes a length takes a percentage, and the reverse.</remarks>
public readonly record struct Percentage
{
    private readonly string? _text;

    internal Percentage(double value) => _text = value.ToString(CultureInfo.InvariantCulture) + "%";

    /// <summary>The percentage as CSS writes it: <c>50%</c>.</summary>
    public override string ToString() => _text ?? "0%";
}
