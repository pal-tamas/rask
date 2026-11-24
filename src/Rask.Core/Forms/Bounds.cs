namespace Rask.Core.Forms;

// The tightest of two limits, either of which may be unsaid: the lower of two "at most"s, the higher of two "at least"s.
internal static class Bounds
{
    internal static T? Least<T>(T? a, T? b)
        where T : struct, IComparable<T>
    {
        if (a is not { } x)
        {
            return b;
        }

        return b is { } y && y.CompareTo(x) < 0 ? y : x;
    }

    internal static T? Most<T>(T? a, T? b)
        where T : struct, IComparable<T>
    {
        if (a is not { } x)
        {
            return b;
        }

        return b is { } y && y.CompareTo(x) > 0 ? y : x;
    }
}
