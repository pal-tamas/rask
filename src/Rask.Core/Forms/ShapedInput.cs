namespace Rask.Core.Forms;

// The input type a field's shape asks for (FieldShape.Input), where it fits what the member holds: any of them for a
// text, and a date for a moment ([DataType(DataType.Date)] on a DateTime: the day of it, at midnight). An email input
// over a number or a time over a moment would show nothing the member holds, so those keep the type's own input.
internal static class ShapedInput
{
    internal static InputType? For(FieldShape? shape, Type bound)
    {
        if (shape?.Input is not { } input)
        {
            return null;
        }

        var shown = Rask.Values.ValueFields.HeldOr(bound);
        var held = Nullable.GetUnderlyingType(shown) ?? shown;
        if (held == typeof(string))
        {
            return input;
        }

        return input == InputType.Date && held == typeof(DateTime) ? input : null;
    }
}
