using Rask.Values;

namespace Rask.Core.Tests.Values;

// F-28: a total split into shares of a step whose sum is the total exactly.
public sealed class AllocationTests
{
    private static readonly decimal[] Steps = [1m, 0.1m, 0.01m, 0.001m, 0.05m, 0.25m];

    [Fact]
    public void Equal_parts_give_the_remainder_to_the_first_ones()
    {
        var total = 100m;

        var shares = Allocation.Split(total, 3, step: 0.01m);

        Assert.Equal([33.34m, 33.33m, 33.33m], shares);
    }

    [Fact]
    public void The_remainder_goes_to_the_last_parts_when_asked()
    {
        var total = 0.05m;

        var shares = Allocation.Split(total, 3, step: 0.01m, remainder: Remainder.ToLast);

        Assert.Equal([0.01m, 0.02m, 0.02m], shares);
    }

    [Fact]
    public void The_largest_remainder_goes_to_the_part_that_lost_most_to_rounding()
    {
        decimal[] weights = [1m, 2m, 7m];

        var shares = Allocation.Split(1m, weights, step: 0.1m, remainder: Remainder.ToLargest);

        Assert.Equal([0.1m, 0.2m, 0.7m], shares);
        Assert.Equal([0m, 1m, 0m], Allocation.Split(1m, [1m, 2m, 1m], step: 1m, remainder: Remainder.ToLargest));
    }

    [Fact]
    public void A_largest_remainder_tie_goes_to_the_earlier_part()
    {
        decimal[] weights = [1m, 1m, 1m];

        var shares = Allocation.Split(2m, weights, step: 1m, remainder: Remainder.ToLargest);

        Assert.Equal([1m, 1m, 0m], shares);
    }

    [Fact]
    public void A_part_of_weight_zero_gets_nothing_not_even_the_remainder()
    {
        decimal[] weights = [0m, 1m, 1m];

        var shares = Allocation.Split(1m, weights, step: 1m);

        Assert.Equal([0m, 1m, 0m], shares);
    }

    [Fact]
    public void A_negative_total_splits_into_negative_shares()
    {
        var total = -100m;

        var shares = Allocation.Split(total, 3, step: 0.01m);

        Assert.Equal([-33.34m, -33.33m, -33.33m], shares);
    }

    [Fact]
    public void A_total_off_the_step_keeps_its_last_fraction_on_the_first_taker()
    {
        var total = 10.005m;

        var shares = Allocation.Split(total, 2, step: 0.01m);

        Assert.Equal([5.005m, 5.00m], shares);
        Assert.Equal(total, shares.Sum());
    }

    [Fact]
    public void Nonsense_arguments_are_refused()
    {
        decimal[] negative = [1m, -1m];

        var noParts = Record.Exception(() => Allocation.Split(1m, 0, step: 0.01m));
        var noStep = Record.Exception(() => Allocation.Split(1m, 2, step: 0m));
        var allZero = Record.Exception(() => Allocation.Split(1m, [0m, 0m], step: 0.01m));
        var debt = Record.Exception(() => Allocation.Split(1m, negative, step: 0.01m));

        Assert.IsType<ArgumentOutOfRangeException>(noParts);
        Assert.IsType<ArgumentOutOfRangeException>(noStep);
        Assert.IsType<ArgumentException>(allZero);
        Assert.IsType<ArgumentException>(debt);
    }

    [Fact]
    public void Any_split_sums_to_the_total_and_keeps_every_share_within_a_step_of_its_exact_part()
    {
        var random = new Random(28);
        var remainders = Enum.GetValues<Remainder>();

        var failures = new List<string>();
        for (var run = 0; run < 5000; run++)
        {
            var step = Steps[random.Next(Steps.Length)];
            var total = random.Next(-1_000_000, 1_000_000) * step;
            var weights = Enumerable.Range(0, random.Next(1, 12)).Select(_ => (decimal)random.Next(0, 1000) / 7).ToArray();
            if (weights.All(w => w == 0))
            {
                weights[0] = 1m;
            }

            var remainder = remainders[random.Next(remainders.Length)];
            var shares = Allocation.Split(total, weights, step, remainder);
            var again = Allocation.Split(total, weights, step, remainder);
            Check(failures, total, weights, step, shares, again);
        }

        Assert.Empty(failures);
    }

    private static void Check(List<string> failures, decimal total, decimal[] weights, decimal step, IReadOnlyList<decimal> shares, IReadOnlyList<decimal> again)
    {
        var weight = weights.Sum();
        var said = $"{total} by [{string.Join(", ", weights)}] in {step}";
        if (shares.Sum() != total)
        {
            failures.Add($"{said}: sums to {shares.Sum()}");
        }

        if (!shares.SequenceEqual(again))
        {
            failures.Add($"{said}: not the same twice");
        }

        for (var i = 0; i < shares.Count; i++)
        {
            var exact = total * weights[i] / weight;
            if (shares[i] % step != 0 || Math.Abs(shares[i] - exact) > step || (weights[i] == 0 && shares[i] != 0))
            {
                failures.Add($"{said}: share {i} is {shares[i]}, exactly {exact}");
            }
        }
    }
}
