using Rask.Values;

namespace Rask.Testing;

/// <summary>A whole number an <see cref="Integer" /> accepts: its least, or 1 within its greatest.</summary>
internal static class IntegerSample
{
    public static long For(IntegerRules rules) =>
        rules.Min ?? (rules.Max is { } max && max < 1 ? max : 1);
}
