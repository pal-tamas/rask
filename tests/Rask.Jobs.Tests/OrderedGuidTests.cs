using Rask.Batteries;

namespace Rask.Background.Tests;

/// <summary>The keys Rask makes for its own rows (jobs, outbox, mail, logs) before the insert, now that no store numbers them.</summary>
public sealed class OrderedGuidTests
{
    [Fact]
    public void A_new_key_is_a_version_7_Guid_later_than_every_one_made_before_it()
    {
        var made = new List<Guid>();

        for (var i = 0; i < 10_000; i++)
        {
            made.Add(OrderedGuid.New());
        }

        Assert.All(made, static id => Assert.Equal(7, id.Version));
        Assert.Equal(made.Order(), made);
        Assert.Equal(made.Count, made.Distinct().Count());
    }

    [Fact]
    public void A_numbered_row_s_key_reads_as_its_millisecond_then_its_number()
    {
        var at = new DateTimeOffset(2026, 11, 27, 9, 30, 0, 123, TimeSpan.Zero);

        var first = OrderedGuid.Of(at, 41);
        var next = OrderedGuid.Of(at, 42);
        var later = OrderedGuid.Of(at.AddMilliseconds(1), 1);

        Assert.Equal(new Guid("01a21c57-1a3b-7000-8000-000000000029"), first);
        Assert.Equal(7, first.Version);
        Assert.True(first < next);
        Assert.True(next < later);
        Assert.Equal(first, OrderedGuid.Of(at, 41));
    }
}
