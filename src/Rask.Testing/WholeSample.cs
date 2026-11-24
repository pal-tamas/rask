using Rask.Values;

namespace Rask.Testing;

/// <summary>A whole number a <see cref="Whole" /> accepts: its least, or 1 within its greatest.</summary>
internal static class WholeSample
{
    public static long For(WholeRules rules) =>
        rules.Min ?? (rules.Max is { } max && max < 1 ? max : 1);
}
