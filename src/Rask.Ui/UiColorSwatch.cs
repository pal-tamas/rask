namespace Rask;

/// <summary>
///     One colour of a <see cref="UiColorPicker" />'s palette: an entry of Flux's <c>:swatches</c> array.
/// </summary>
/// <remarks>
///     A string is a swatch and so is a pair, as in Flux: <c>.Swatches(["#ef4444", "#22c55e"])</c> or
///     <c>.Swatches([("#ef4444", "Red"), ("#22c55e", "Green")])</c>.
/// </remarks>
/// <param name="Value">The colour, as any CSS colour string.</param>
/// <param name="Label">What assistive tech calls it. The value itself when unset.</param>
public readonly record struct UiColorSwatch(string Value, string? Label = null)
{
    /// <summary>A swatch from its colour alone.</summary>
    public static implicit operator UiColorSwatch(string value) => new(value);

    /// <summary>A swatch from Flux's <c>[value, label]</c> pair.</summary>
    public static implicit operator UiColorSwatch((string Value, string Label) pair) => new(pair.Value, pair.Label);

    /// <summary>A swatch from its colour alone, for a language without conversion operators.</summary>
    public static UiColorSwatch FromString(string value) => new(value);

    /// <summary>A swatch from a pair, for a language without conversion operators.</summary>
    public static UiColorSwatch FromValueTuple((string Value, string Label) pair) => new(pair.Value, pair.Label);
}
