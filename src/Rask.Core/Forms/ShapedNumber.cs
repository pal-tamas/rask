using System.Globalization;

namespace Rask.Core.Forms;

// A number input's attributes from its field's shape: the bounds a Number or a Whole declares, and the step its scale
// calls for. Null for a shape that is no number's, and for what it does not declare.
internal static class ShapedNumber
{
    internal static string? Min(FieldShape? shape) => IsNumber(shape) ? Written(shape!.Min) : null;

    internal static string? Max(FieldShape? shape) => IsNumber(shape) ? Written(shape!.Max) : null;

    // 10^-Scale: a whole number steps by one, a number of two decimals by 0.01; no scale leaves the input's own step.
    internal static string? Step(FieldShape? shape) => shape switch
    {
        { Kind: FieldKind.Whole } => "1",
        { Kind: FieldKind.Number, Scale: 0 } => "1",
        { Kind: FieldKind.Number, Scale: { } scale } => "0." + new string('0', scale - 1) + "1",
        _ => null,
    };

    private static bool IsNumber(FieldShape? shape) => shape is { Kind: FieldKind.Number or FieldKind.Whole };

    private static string? Written(decimal? bound) => bound?.ToString(CultureInfo.InvariantCulture);
}
