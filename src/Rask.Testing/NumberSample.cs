using Rask.Values;

namespace Rask.Testing;

/// <summary>A number a <see cref="Number" />'s least, greatest and scale accept: its least, or 1 within its greatest.</summary>
internal static class NumberSample
{
    public static decimal For(NumberRules rules)
    {
        var number = rules.Min ?? (rules.Max is { } max && max < 1 ? max : 1m);
        if (rules.Scale is not { } places || decimal.Round(number, places) == number)
        {
            return number;
        }

        // A least of 0.005 at two places: the first number at that scale above it, unless that passes the greatest.
        var up = decimal.Round(number, places, MidpointRounding.ToPositiveInfinity);
        return rules.Max is { } most && up > most ? decimal.Round(number, places, MidpointRounding.ToNegativeInfinity) : up;
    }
}
