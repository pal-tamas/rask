namespace Rask.Values;

/// <summary>
///     Splits a total into shares rounded to a step, whose sum is the total exactly:
///     <c>Allocation.Split(100m, 3, step: 0.01m)</c> is <c>[33.34, 33.33, 33.33]</c>.
/// </summary>
/// <remarks>
///     A pure computation — no database, no clock, no culture: the same arguments always give the same shares. Each share is
///     its exact part rounded toward zero to a whole number of steps; what that leaves over goes, a step at a time, to the
///     parts <see cref="Remainder" /> names. A total that is not itself a whole number of steps (<c>10.005</c> split by
///     <c>0.01</c>) keeps its last fraction on the part that takes the first step of the remainder, so the sum is still
///     the total.
/// </remarks>
public static class Allocation
{
    /// <summary>
    ///     Splits <paramref name="total" /> into <paramref name="parts" /> equal shares, each a whole number of
    ///     <paramref name="step" />s, that add up to it exactly.
    /// </summary>
    /// <param name="total">What is split; negative splits into negative shares.</param>
    /// <param name="parts">How many shares; at least one.</param>
    /// <param name="step">The unit every share is a whole number of — <c>0.01m</c> for hundredths; above zero.</param>
    /// <param name="remainder">Which shares take what rounding down left over.</param>
    /// <returns>The shares, in order; their sum is <paramref name="total" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="parts" /> or <paramref name="step" /> is not above zero.</exception>
    public static IReadOnlyList<decimal> Split(decimal total, int parts, decimal step, Remainder remainder = Remainder.ToFirst)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parts);
        var weights = new decimal[parts];
        Array.Fill(weights, 1m);
        return Split(total, weights, step, remainder);
    }

    /// <summary>
    ///     Splits <paramref name="total" /> in proportion to <paramref name="weights" />, each share a whole number of
    ///     <paramref name="step" />s, adding up to it exactly. A part of weight zero gets zero, and none of the remainder.
    /// </summary>
    /// <param name="total">What is split; negative splits into negative shares.</param>
    /// <param name="weights">One weight per share, none negative and at least one above zero.</param>
    /// <param name="step">The unit every share is a whole number of — <c>0.01m</c> for hundredths; above zero.</param>
    /// <param name="remainder">Which shares take what rounding down left over.</param>
    /// <returns>The shares, in the order of <paramref name="weights" />; their sum is <paramref name="total" />.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="weights" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="step" /> is not above zero.</exception>
    /// <exception cref="ArgumentException">A weight is negative, or none is above zero.</exception>
    public static IReadOnlyList<decimal> Split(
        decimal total, IReadOnlyList<decimal> weights, decimal step, Remainder remainder = Remainder.ToFirst)
    {
        ArgumentNullException.ThrowIfNull(weights);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(step);
        var weight = TotalWeight(weights);

        // Split the magnitude and give the sign back at the end, so rounding "down" is toward zero on either side.
        var magnitude = Math.Abs(total);
        var shares = new decimal[weights.Count];
        var lost = new decimal[weights.Count];
        var left = magnitude;
        for (var i = 0; i < shares.Length; i++)
        {
            var exact = weights[i] == weight ? magnitude : magnitude * weights[i] / weight;
            shares[i] = Math.Floor(exact / step) * step;
            lost[i] = exact - shares[i];
            left -= shares[i];
        }

        HandOut(shares, left, step, Takers(weights, lost, remainder));
        if (total < 0)
        {
            for (var i = 0; i < shares.Length; i++)
            {
                shares[i] = -shares[i];
            }
        }

        return shares;
    }

    private static decimal TotalWeight(IReadOnlyList<decimal> weights)
    {
        var sum = 0m;
        foreach (var weight in weights)
        {
            if (weight < 0)
            {
                throw new ArgumentException("A weight cannot be negative — a share is a part of the total, not a debt.", nameof(weights));
            }

            sum += weight;
        }

        return sum > 0
            ? sum
            : throw new ArgumentException("At least one weight must be above zero — there is nobody to give the total to.", nameof(weights));
    }

    // The parts that may take a step of the remainder, in the order they take it: only those of a weight above zero.
    private static int[] Takers(IReadOnlyList<decimal> weights, decimal[] lost, Remainder remainder)
    {
        var takers = Enumerable.Range(0, weights.Count).Where(i => weights[i] > 0);
        return remainder switch
        {
            Remainder.ToFirst => [.. takers],
            Remainder.ToLast => [.. takers.Reverse()],
            Remainder.ToLargest => [.. takers.OrderByDescending(i => lost[i]).ThenBy(i => i)],
            _ => throw new ArgumentOutOfRangeException(nameof(remainder), remainder, "Name a Remainder: ToFirst, ToLast or ToLargest."),
        };
    }

    // Whole steps first, round and round the takers; then what is under a step - a total off the step's grid, or the
    // last digit of a division - on the first taker, so the sum is the total exactly.
    private static void HandOut(decimal[] shares, decimal left, decimal step, int[] takers)
    {
        var steps = left > 0 ? Math.Floor(left / step) : 0m;
        var each = Math.Floor(steps / takers.Length);
        var extra = steps - (each * takers.Length);
        for (var k = 0; k < takers.Length; k++)
        {
            var given = (each + (k < extra ? 1 : 0)) * step;
            shares[takers[k]] += given;
            left -= given;
        }

        shares[takers[0]] += left;
    }
}
