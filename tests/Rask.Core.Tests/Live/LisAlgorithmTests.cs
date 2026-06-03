using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Regression guard for FrameDiffer.ComputeLisIndexSet — the keyed-reorder MOVES path
// at FrameDiffer.cs:568. The previous O(N²) DP was replaced with O(N log N) patience
// sorting; both must return an INCREASING subsequence of OPTIMAL LENGTH (any optimal LIS
// produces the same move count, so the choice between ties is benign — but length must
// be optimal or extra MoveSubtree ops will leak into the diff stream).
public class LisAlgorithmTests
{
    [Theory]
    [InlineData(new int[0], 0)]
    [InlineData(new[] { 5 }, 1)]
    [InlineData(new[] { 1, 2, 3, 4, 5 }, 5)]                       // already sorted
    [InlineData(new[] { 5, 4, 3, 2, 1 }, 1)]                       // reversed
    [InlineData(new[] { 3, 1, 4, 1, 5, 9, 2, 6, 5, 3, 5 }, 4)]     // classic test
    [InlineData(new[] { 10, 22, 9, 33, 21, 50, 41, 60, 80 }, 6)]   // Wikipedia LIS example
    [InlineData(new[] { 0, 2, 4, 1, 3, 5 }, 4)]                    // interleaved
    public void ComputeLisIndexSet_ReturnsOptimalLengthIncreasingSubsequence(int[] input, int expectedLisLength)
    {
        var lisIndexes = FrameDiffer.ComputeLisIndexSet(input);

        Assert.Equal(expectedLisLength, lisIndexes.Count);

        // The chosen indexes must yield strictly increasing values in their natural order.
        var ordered = lisIndexes.OrderBy(i => i).ToArray();
        for (var i = 1; i < ordered.Length; i++)
        {
            Assert.True(
                input[ordered[i]] > input[ordered[i - 1]],
                $"LIS values must strictly increase but {input[ordered[i - 1]]} >= {input[ordered[i]]}");
        }
    }

    [Fact]
    public void ComputeLisIndexSet_RandomLargeInput_MatchesNaiveDp()
    {
        // Patience-sort vs naive DP on 200 random elements: both must agree on length.
        // Specific indexes may differ when ties exist, but length is invariant.
        var rng = new Random(42);
        var arr = Enumerable.Range(0, 200).Select(_ => rng.Next(0, 100)).ToArray();

        var fast = FrameDiffer.ComputeLisIndexSet(arr);
        var slow = NaiveLisLength(arr);

        Assert.Equal(slow, fast.Count);
    }

    // O(N²) reference impl — used only by the random-input sanity test above.
    private static int NaiveLisLength(int[] arr)
    {
        var n = arr.Length;
        if (n == 0) return 0;
        var dp = new int[n];
        Array.Fill(dp, 1);
        var best = 1;
        for (var i = 1; i < n; i++)
        {
            for (var j = 0; j < i; j++)
            {
                if (arr[j] < arr[i] && dp[j] + 1 > dp[i])
                {
                    dp[i] = dp[j] + 1;
                }
            }
            if (dp[i] > best) best = dp[i];
        }
        return best;
    }
}
