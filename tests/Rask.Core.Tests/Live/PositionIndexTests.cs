using Rask.Core.Live;

namespace Rask.Core.Tests.Live;

// Isolated correctness fuzz for the order-statistics treap that backs the large keyed-reorder move
// loop. Every operation is checked against a plain List<int> oracle doing the identical mutation, so
// any divergence in RankOf / RemoveAt / InsertAt surfaces immediately — before the structure is ever
// wired into FrameDiffer.
public class PositionIndexTests
{
    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 10)]
    [InlineData(5, 65)]
    [InlineData(6, 100)]
    [InlineData(7, 257)]
    [InlineData(8, 1000)]
    [InlineData(9, 5000)]
    public void Mirrors_a_reference_list_under_random_move_ops(int seed, int n)
    {
        var rng = new Random(seed);
        var reference = new List<int>();
        for (var i = 0; i < n; i++)
        {
            reference.Add(i);
        }

        var index = new PositionIndex();
        index.InitSequence(n);
        try
        {
            Assert.Equal(reference.Count, index.Count);
            for (var v = 0; v < n; v++)
            {
                Assert.Equal(reference.IndexOf(v), index.RankOf(v));
            }

            // Each op mirrors one keyed-reorder move: find a value's rank, detach it, then look up a
            // second value's rank (as the loop does for the anchor) and re-insert at a chosen position.
            for (var op = 0; op < n * 4; op++)
            {
                var value = rng.Next(n);
                var src = reference.IndexOf(value);
                Assert.Equal(src, index.RankOf(value));

                reference.RemoveAt(src);
                Assert.Equal(value, index.RemoveAt(src));
                Assert.Equal(reference.Count, index.Count);

                // Rank of another live value between detach and re-insert (the loop's anchor lookup).
                if (reference.Count > 0)
                {
                    var anchor = reference[rng.Next(reference.Count)];
                    Assert.Equal(reference.IndexOf(anchor), index.RankOf(anchor));
                }

                var dst = rng.Next(reference.Count + 1);
                reference.Insert(dst, value);
                index.InsertAt(dst, value);
                Assert.Equal(reference.Count, index.Count);
            }

            // Whole sequence still agrees, position by position.
            for (var pos = 0; pos < n; pos++)
            {
                Assert.Equal(pos, index.RankOf(reference[pos]));
            }
        }
        finally
        {
            index.Return();
        }
    }
}
