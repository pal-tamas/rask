namespace Rask.Core.Forms;

// The value a bound enum field reads from what arrived: a member's name (any case), or a [Flags] combination of them.
//
// Enum.TryParse alone takes more than that: any number ("99", "-1") and any comma list, so a frame written by hand
// could put a value in the model that no member names and no option offered. What it reads is kept only when the
// enum defines it — for [Flags], when every bit it sets is a member's.
internal static class EnumText
{
    internal static bool TryParse(Type enumType, string raw, out object? value)
    {
        if (Enum.TryParse(enumType, raw, ignoreCase: true, out var parsed) && IsDefined(enumType, parsed))
        {
            value = parsed;
            return true;
        }

        value = null;
        return false;
    }

    private static bool IsDefined(Type enumType, object value)
    {
        if (!enumType.IsDefined(typeof(FlagsAttribute), inherit: false))
        {
            return Enum.IsDefined(enumType, value);
        }

        var bits = Bits(value);
        var known = 0UL;
        foreach (var member in Enum.GetValuesAsUnderlyingType(enumType))
        {
            known |= Bits(member);
        }

        return (bits & ~known) == 0;
    }

    // The value's bits whatever its underlying type: a negative sbyte/short/int/long sign-extends, as the enum's own
    // bitwise operators see it.
    private static ulong Bits(object value) => Type.GetTypeCode(value.GetType()) switch
    {
        TypeCode.SByte or TypeCode.Int16 or TypeCode.Int32 or TypeCode.Int64 => unchecked((ulong)Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture)),
        _ => Convert.ToUInt64(value, System.Globalization.CultureInfo.InvariantCulture),
    };
}
